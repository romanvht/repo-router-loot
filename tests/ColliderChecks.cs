using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RouterLoot.Assets;

static class ColliderChecks
{
    static readonly Vector3 Scale = new(-2, .15f, .4f);
    static readonly Quaternion Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .6f);
    static readonly Vector3 Translation = new(.2f, .3f, .4f);
    static readonly Quaternion ParentRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, .25f);

    static void Require(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidDataException(message);
        }
    }

    public static void Run(string assets, string output)
    {
        const string id = "collider_fixture";
        string file = Path.Combine(assets, "models", id + ".glb");
        var fixture = Fixture();
        File.WriteAllBytes(file, fixture);
        var model = GlbImporter.Load(file);
        Require(model.parts.Count == 1 && model.materials.Count == 1 && model.colliders.Count == 2,
            "COL_ geometry or materials leaked into visuals");

        var parent = Matrix4x4.CreateScale(2) * Matrix4x4.CreateFromQuaternion(ParentRotation)
            * Matrix4x4.CreateTranslation(4, 5, 6) * Matrix4x4.CreateScale(1, 1, -1);
        Matrix4x4 Transform(Vector3 scale, Vector3 translation) => Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(translation) * parent;
        var visible = Corners().Select(p => Vector3.Transform(p, Transform(Scale, Translation))).ToArray();
        var lo = visible.Aggregate(Vector3.Min);
        var hi = visible.Aggregate(Vector3.Max);
        var origin = new Vector3((lo.X + hi.X) / 2, lo.Y, (lo.Z + hi.Z) / 2);
        float factor = .42f / (hi.X - lo.X);
        Require(Vector3.Distance(V(model.size), (hi - lo) * factor) < 1e-6,
            "COL_ geometry affected model dimensions");

        for (int i = 0; i < 2; i++)
        {
            var transform = i == 0 ? Transform(Scale * 1.1f, Translation) : Transform(new Vector3(.2f), new Vector3(10, 0, 0));
            var expected = Corners().Select(p => (Vector3.Transform(p, transform) - origin) * factor).ToArray();
            var box = model.colliders[i];
            var q = new Quaternion(box.rotation[0], box.rotation[1], box.rotation[2], box.rotation[3]);
            var actual = Corners().Select(p => Vector3.Transform(p * V(box.size), q) + V(box.center)).ToArray();
            Require(expected.All(p => actual.Any(a => Vector3.Distance(p, a) < 1e-5)),
                "Collider corners differ after nested rotation, reflection, scaling or centering");
        }

        var smaller = GlbImporter.Load(file, .21f);
        Require(model.colliders.Zip(smaller.colliders).All(pair =>
            Vector3.Distance(V(pair.First.center) / 2, V(pair.Second.center)) < 1e-6
            && Vector3.Distance(V(pair.First.size) / 2, V(pair.Second.size)) < 1e-6
            && pair.First.rotation.SequenceEqual(pair.Second.rotation)), "Collider did not follow width");

        ImportPipeline.Prepare(assets, output);
        var runtime = PipelineChecks.ReadModel(output, id);
        Require(JsonSerializer.Serialize(runtime.colliders) == JsonSerializer.Serialize(model.colliders),
            "Model payload lost collider data");
        string catalogPath = Path.Combine(assets, "catalog.json");
        string catalog = File.ReadAllText(catalogPath);
        var entry = JsonNode.Parse(catalog)!["routers"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == id)!;
        Require(entry.AsObject().Select(p => p.Key).Order().SequenceEqual(new[] { "id", "mass", "max", "min", "name", "width" }),
            "Unexpected catalog fields");
        AssetChecks.Check(output);

        string generated = Snapshot(output);
        foreach (string invalid in new[] { "shear", "zero", "empty", "children", "uncovered", "only_colliders" })
        {
            File.WriteAllBytes(file, Fixture(invalid));
            bool rejected = false;
            try
            {
                ImportPipeline.Prepare(assets, output);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Require(rejected, $"Invalid collider accepted: {invalid}");
            Require(File.ReadAllText(catalogPath) == catalog && Snapshot(output) == generated,
                "Rejected collider import changed generated files");
        }

        File.WriteAllBytes(file, Fixture("no_colliders"));
        ImportPipeline.Prepare(assets, output);
        Require(PipelineChecks.ReadModel(output, id).colliders.Count == 1, "Removing COL_ objects did not generate a bounding box");
        Require(File.ReadAllText(catalogPath) == catalog, "Model edit changed catalog settings");
        File.WriteAllBytes(file, fixture);
        ImportPipeline.Prepare(assets, output);
        Require(PipelineChecks.ReadModel(output, id).colliders.Count == 2, "Reimport did not restore embedded colliders");
        File.Delete(file);
        ImportPipeline.Prepare(assets, output);
        Console.WriteLine("PASS: embedded boxes, rotated corners, materials, width, payload, invalid imports and model replacement");
    }

    static Vector3 V(float[] values) => new(values[0], values[1], values[2]);

    static IEnumerable<Vector3> Corners() => Enumerable.Range(0, 8).Select(i => new Vector3(
        (i & 1) == 0 ? -.5f : .5f, (i & 2) == 0 ? -.5f : .5f, (i & 4) == 0 ? -.5f : .5f));

    static string Snapshot(string directory) => string.Join("\n", Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Order().Select(p => p + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))));

    static byte[] Fixture(string variant = "")
    {
        using var bin = new MemoryStream();
        using (var writer = new BinaryWriter(bin, Encoding.UTF8, true))
        {
            foreach (var p in Corners())
            {
                writer.Write(p.X);
                writer.Write(p.Y);
                writer.Write(p.Z);
            }

            foreach (ushort i in new ushort[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 })
            {
                writer.Write(i);
            }
        }

        var json = JsonNode.Parse("""
        {
          "asset":{"version":"2.0"}, "scene":0, "scenes":[{"nodes":[0]}],
          "nodes":[{"translation":[4,5,6],"scale":[2,2,2],"children":[1,2,3]},
            {"name":"Router","mesh":0},{"name":"COL_body","mesh":1},{"name":"col_extra","mesh":1}],
          "buffers":[{"byteLength":168}],
          "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":96},{"buffer":0,"byteOffset":96,"byteLength":72}],
          "accessors":[{"bufferView":0,"componentType":5126,"count":8,"type":"VEC3","min":[-0.5,-0.5,-0.5],"max":[0.5,0.5,0.5]},
            {"bufferView":1,"componentType":5123,"count":36,"type":"SCALAR"}],
          "materials":[{"pbrMetallicRoughness":{"baseColorFactor":[1,1,1,1]}}, {"alphaMode":"BLEND"}],
          "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"material":0}]},
            {"primitives":[{"attributes":{"POSITION":0},"indices":1,"material":1}]}]
        }
        """)!;
        var nodes = json["nodes"]!.AsArray();
        nodes[0]!["rotation"] = JsonSerializer.SerializeToNode(new[] { ParentRotation.X, ParentRotation.Y, ParentRotation.Z, ParentRotation.W });
        for (int i = 1; i <= 3; i++)
        {
            var scale = i == 1 ? Scale : i == 2 ? Scale * 1.1f : new Vector3(.2f);
            var translation = i == 3 ? new Vector3(10, 0, 0) : Translation;
            nodes[i]!["scale"] = JsonSerializer.SerializeToNode(new[] { scale.X, scale.Y, scale.Z });
            nodes[i]!["translation"] = JsonSerializer.SerializeToNode(new[] { translation.X, translation.Y, translation.Z });
            nodes[i]!["rotation"] = JsonSerializer.SerializeToNode(new[] { Rotation.X, Rotation.Y, Rotation.Z, Rotation.W });
        }

        switch (variant)
        {
            case "shear": nodes[0]!["scale"] = new JsonArray(2, 3, 1); break;
            case "zero": nodes[2]!["scale"] = new JsonArray(0, 1, 1); break;
            case "empty": nodes[2]!.AsObject().Remove("mesh"); break;
            case "children":
                nodes.Add(new JsonObject());
                nodes[2]!["children"] = new JsonArray(4);
                break;
            case "uncovered": nodes[2]!["scale"] = new JsonArray(.01, .01, .01); break;
            case "only_colliders": nodes[0]!["children"] = new JsonArray(2, 3); break;
            case "no_colliders": nodes[0]!["children"] = new JsonArray(1); break;
        }

        return PipelineChecks.PackGlb(json, bin.ToArray());
    }
}
