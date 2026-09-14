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

        result.size = [size.X, size.Y, size.Z];
        Require(result.size.All(x => float.IsFinite(x) && x > 0 && x <= 2), "Model must have nonzero dimensions no greater than 2 metres; adjust catalog width");

        return result;
    }

    static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

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
