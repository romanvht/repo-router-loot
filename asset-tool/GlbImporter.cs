using System.Numerics;
using SharpGLTF.Schema2;
using static RouterLoot.Assets.AssetJson;

namespace RouterLoot.Assets;

public static class GlbImporter
{
    public static RuntimeModel Load(string path, float width = .42f)
    {
        Require(float.IsFinite(width) && width > 0 && width <= 2, "width must be between 0 and 2 metres");

        var context = ReadContext.Create(uri => throw new InvalidDataException($"External resource '{uri}': embed textures and buffers in the GLB"));
        using var stream = File.OpenRead(path);
        var source = context.ReadBinarySchema2(stream);

        Require(source.LogicalAnimations.Count == 0 && source.LogicalSkins.Count == 0, "Use a static GLB without animations or skins");

        var supported = new[] { "KHR_mesh_quantization", "KHR_texture_transform" };

        Require(!source.ExtensionsRequired.Except(supported).Any(), "Unsupported required GLB extension: " + string.Join(", ", source.ExtensionsRequired.Except(supported)));

        var scene = source.DefaultScene ?? source.LogicalScenes.SingleOrDefault();

        Require(scene != null, "GLB must have a default scene");

        var result = new RuntimeModel { id = Path.GetFileNameWithoutExtension(path) };
        var materialMap = new Dictionary<int, int>();

        foreach (var node in Node.Flatten(scene!))
        {
            if (node.Name?.StartsWith("COL_", StringComparison.OrdinalIgnoreCase) == true)
            {
                result.colliders.Add(ReadCollider(node));
                continue;
            }

            if (node.Mesh == null)
            {
                continue;
            }

            var transform = node.WorldMatrix * Matrix4x4.CreateScale(1, 1, -1);

            Require(Matrix4x4.Invert(transform, out var inverse), "A mesh has a zero-scale transform");

            var normalTransform = Matrix4x4.Transpose(inverse);
            bool reverse = transform.GetDeterminant() < 0;

            foreach (var primitive in node.Mesh.Primitives)
            {
                Require(primitive.MorphTargetsCount == 0, "Apply morph targets before exporting a static GLB");
                Require(primitive.DrawPrimitiveType is PrimitiveType.TRIANGLES or PrimitiveType.TRIANGLE_STRIP or PrimitiveType.TRIANGLE_FAN,
                    "Only triangle surfaces are supported");

                var mat = primitive.Material;
                int key = mat?.LogicalIndex ?? -1;

                if (!materialMap.TryGetValue(key, out int mi))
                {
                    mi = result.materials.Count;
                    result.materials.Add(ReadMaterial(mat));
                    materialMap.Add(key, mi);
                }

                var positions = primitive.GetVertexAccessor("POSITION").AsVector3Array();
                var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                var channel = mat?.FindChannel("BaseColor");
                int uvSet = channel?.TextureTransform?.TextureCoordinateOverride ?? channel?.TextureCoordinate ?? 0;
                var uv = primitive.GetVertexAccessor("TEXCOORD_" + uvSet)?.AsVector2Array();

                Require(channel?.Texture == null || uv != null, "A textured primitive has no matching UV coordinates");

                var part = new RuntimePart { name = node.Name ?? node.Mesh.Name ?? result.id, material = mi };
                var vertexMap = normals == null ? null : Enumerable.Repeat(-1, positions.Count).ToArray();

                foreach (var (a, b, c) in primitive.GetTriangleIndices())
                {
                    int[] indices = reverse ? [a, c, b] : [a, b, c];
                    var points = indices.Select(i => Vector3.Transform(positions[i], transform)).ToArray();
                    var cross = Vector3.Cross(points[1] - points[0], points[2] - points[0]);

                    if (cross.LengthSquared() < 1e-20f)
                    {
                        continue;
                    }

                    var flatNormal = Vector3.Normalize(cross);

                    for (int j = 0; j < 3; j++)
                    {
                        if (vertexMap != null && vertexMap[indices[j]] >= 0)
                        {
                            part.triangles.Add(vertexMap[indices[j]]);
                            continue;
                        }

                        var normal = normals == null ? flatNormal : Vector3.Normalize(Vector3.TransformNormal(normals[indices[j]], normalTransform));
                        var tex = uv == null ? Vector2.Zero : uv[indices[j]];

                        if (channel?.TextureTransform is { } uvTransform)
                        {
                            tex = Vector2.Transform(tex, uvTransform.Matrix);
                        }

                        int vertex = part.positions.Count / 3;

                        part.uv.AddRange([tex.X, 1 - tex.Y]);
                        part.positions.AddRange([points[j].X, points[j].Y, points[j].Z]);
                        part.normals.AddRange([normal.X, normal.Y, normal.Z]);
                        part.triangles.Add(vertex);

                        if (vertexMap != null)
                        {
                            vertexMap[indices[j]] = vertex;
                        }
                    }
                }

                if (mat?.DoubleSided == true)
                {
                    int count = part.positions.Count / 3;
                    int indexCount = part.triangles.Count;

                    part.positions.AddRange(part.positions.ToArray());
                    part.uv.AddRange(part.uv.ToArray());
                    part.normals.AddRange(part.normals.Select(x => -x).ToArray());

                    for (int i = 0; i < indexCount; i += 3)
                    {
                        part.triangles.AddRange([count + part.triangles[i], count + part.triangles[i + 2], count + part.triangles[i + 1]]);
                    }
                }

                Require(part.positions.Count > 0 && part.positions.Count / 3 <= 150000, "Empty mesh or more than 150000 vertices in a primitive; simplify the model");
                result.parts.Add(part);
            }
        }

        Require(result.parts.Count > 0, "No mesh surfaces in the default scene");

        var all = result.Points.ToArray();

        Require(all.All(Finite), "Non-finite model coordinates");

        var lo = all.Aggregate(Vector3.Min);
        var hi = all.Aggregate(Vector3.Max);

        Require(hi.X > lo.X, "Model width is zero");

        float scale = width / (hi.X - lo.X);
        var origin = new Vector3((lo.X + hi.X) / 2, lo.Y, (lo.Z + hi.Z) / 2);

        foreach (var part in result.parts)
        {
            for (int i = 0; i < part.positions.Count; i += 3)
            {
                var point = (new Vector3(part.positions[i], part.positions[i + 1], part.positions[i + 2]) - origin) * scale;

                part.positions[i] = point.X;
                part.positions[i + 1] = point.Y;
                part.positions[i + 2] = point.Z;
            }

            Require(part.normals.All(float.IsFinite) && part.uv.All(float.IsFinite), "Non-finite normals or UVs");
        }

        var size = (hi - lo) * scale;

        foreach (var box in result.colliders)
        {
            var center = (new Vector3(box.center[0], box.center[1], box.center[2]) - origin) * scale;
            box.center = [center.X, center.Y, center.Z];
            box.size = box.size.Select(x => x * scale).ToArray();
        }

        result.size = [size.X, size.Y, size.Z];
        Require(result.size.All(x => float.IsFinite(x) && x > 0 && x <= 2), "Model must have nonzero dimensions no greater than 2 metres; adjust catalog width");

        if (result.colliders.Count == 0)
        {
            result.colliders.Add(new ImportedCollider
            {
                center = [0, size.Y / 2, 0],
                size = result.size.Select(x => Math.Max(x, .014f)).ToArray()
            });
        }

        ValidateColliders(result);

        return result;
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    static ImportedCollider ReadCollider(Node node)
    {
        string label = $"Collider '{node.Name}': ";
        Require(node.Mesh != null && !node.VisualChildren.Any(), label + "use a mesh object without children, not an empty/group");
        Require(node.Mesh.Primitives.All(p => p.MorphTargetsCount == 0), label + "apply morph targets before exporting");
        var points = node.Mesh.Primitives.SelectMany(p => p.GetVertexAccessor("POSITION").AsVector3Array()).ToArray();
        Require(points.Length > 0 && points.All(Finite), label + "empty mesh or non-finite coordinates");
        var lo = points.Aggregate(Vector3.Min);
        var hi = points.Aggregate(Vector3.Max);
        var extent = hi - lo;
        Require(extent.X > 0 && extent.Y > 0 && extent.Z > 0, label + "box must have nonzero dimensions on every axis");

        var transform = node.WorldMatrix * Matrix4x4.CreateScale(1, 1, -1);
        var x = Vector3.TransformNormal(Vector3.UnitX, transform);
        var y = Vector3.TransformNormal(Vector3.UnitY, transform);
        var z = Vector3.TransformNormal(Vector3.UnitZ, transform);
        var lengths = new Vector3(x.Length(), y.Length(), z.Length());
        Require(Finite(lengths) && lengths.X > 0 && lengths.Y > 0 && lengths.Z > 0, label + "invalid or zero scale");
        x /= lengths.X;
        y /= lengths.Y;
        z /= lengths.Z;
        Require(Math.Abs(Vector3.Dot(x, y)) < 1e-4f && Math.Abs(Vector3.Dot(x, z)) < 1e-4f
            && Math.Abs(Vector3.Dot(y, z)) < 1e-4f,
            label + "sheared transform cannot form a box; remove parent shear before exporting");
        if (Vector3.Dot(Vector3.Cross(x, y), z) < 0)
        {
            z = -z;
        }

        var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        var center = Vector3.Transform((lo + hi) / 2, transform);
        var size = extent * lengths;
        Require(Finite(center) && Finite(size), label + "non-finite bounds");
        return new ImportedCollider
        {
            center = [center.X, center.Y, center.Z],
            size = [size.X, size.Y, size.Z],
            rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W]
        };
    }

    static void ValidateColliders(RuntimeModel model)
    {
        Require(model.colliders.All(b => b.center.All(float.IsFinite) && b.size.All(x => float.IsFinite(x) && x > 0)
            && b.rotation.All(float.IsFinite)), "Invalid collider bounds or rotation");
        var boxes = model.colliders.Select(b => (
            center: new Vector3(b.center[0], b.center[1], b.center[2]),
            half: new Vector3(b.size[0], b.size[1], b.size[2]) / 2 + new Vector3(.015f),
            inverse: Quaternion.Inverse(new Quaternion(b.rotation[0], b.rotation[1], b.rotation[2], b.rotation[3])))).ToArray();
        Require(model.Points.All(p => boxes.Any(b =>
        {
            var local = Vector3.Abs(Vector3.Transform(p - b.center, b.inverse));
            return local.X <= b.half.X && local.Y <= b.half.Y && local.Z <= b.half.Z;
        })), "COL_ boxes do not cover the model; adjust their bounds in the GLB");
    }

    static RuntimeMaterial ReadMaterial(Material? material)
    {
        if (material == null)
        {
            return new RuntimeMaterial { name = "Default" };
        }

        Require(material.Alpha == AlphaMode.OPAQUE, "Use opaque materials (transparent GLB materials are not supported)");

        var channel = material.FindChannel("BaseColor");

        Require(channel != null, "Use glTF metallic/roughness materials");

        var color = channel.Value.Color;
        var factors = material.FindChannel("MetallicRoughness");
        var result = new RuntimeMaterial { name = material.Name ?? "Material", color = [color.X, color.Y, color.Z, color.W] };

        result.metallic = factors?.Parameters.FirstOrDefault(p => p.Name == "MetallicFactor")?.Value is float metal ? metal : 1;
        result.roughness = factors?.Parameters.FirstOrDefault(p => p.Name == "RoughnessFactor")?.Value is float rough ? rough : 1;

        if (channel.Value.Texture?.PrimaryImage is { } image)
        {
            var content = image.Content;

            Require(content.IsPng || content.IsJpg, "Embed base color textures as PNG or JPEG");
            result.texture = Convert.ToBase64String(content.Content.Span);
        }

        if (material.Channels.Any(c => c.Key != "BaseColor" && c.Texture != null))
        {
            Console.WriteLine($"NOTE {result.name}: only base color texture is used; other texture maps are ignored");
        }

        return result;
    }
}
