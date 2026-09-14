using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RouterLoot.Assets;

static class PipelineChecks
{
    static void Require(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidDataException(message);
        }
    }

    static string Snapshot(string directory) => string.Join("\n", Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Order().Select(p => Path.GetRelativePath(directory, p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));

    public static void Run(string assets, string output)
    {
        string id = "test_" + Guid.NewGuid().ToString("N");
        string file = Path.Combine(assets, "models", id + ".glb");

        File.WriteAllBytes(file, Fixture());

        var imported = GlbImporter.Load(file);
        var part = imported.parts.Single();

        Vector3 Point(int i) => new(part.positions[i * 3], part.positions[i * 3 + 1], part.positions[i * 3 + 2]);

        var e1 = Point(1) - Point(0);
        var e2 = Point(2) - Point(0);
        var normal = new Vector3(part.normals[0], part.normals[1], part.normals[2]);

        Require(Math.Abs(Vector3.Dot(normal, e1)) < 1e-6 && Math.Abs(Vector3.Dot(normal, e2)) < 1e-6, "Nonuniform normal transform failed");
        Require(Vector3.Dot(Vector3.Cross(e1, e2), normal) > 0, "Mirrored winding is reversed");
        Require(Math.Abs(imported.size[0] - .42f) < 1e-6 && imported.Points.Min(p => p.Y) == 0, "Scale or grounding failed");
        Require(Math.Abs(part.uv[0] - .1f) < 1e-6 && Math.Abs(part.uv[1] - .8f) < 1e-6, "GLB texture V coordinate was not flipped");
        Require(Math.Abs(imported.materials[0].metallic - .25f) < 1e-6 && Math.Abs(imported.materials[0].roughness - .6f) < 1e-6,
            "Material factors were not preserved");
        ImportPipeline.Prepare(assets, output);

        string catalogPath = Path.Combine(assets, "catalog.json");
        var catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!;
        var entry = catalog["routers"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == id)!;

        Require(entry["autoColliders"]!.GetValue<bool>() && entry["colliders"]!.AsArray().Count == 1, "New model was not configured automatically");
        entry["name"] = "Custom Router";
        entry["min"] = 750;
        entry["width"] = .3f;
        File.WriteAllText(catalogPath, catalog.ToJsonString());
        ImportPipeline.Prepare(assets, output);
        catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!;
        entry = catalog["routers"]!.AsArray().Single(s => s!["id"]!.GetValue<string>() == id)!;
        Require(entry["name"]!.GetValue<string>() == "Custom Router" && entry["min"]!.GetValue<int>() == 750, "Custom settings were lost");
        Require(Math.Abs(entry["colliders"]![0]!["size"]![0]!.GetValue<float>() - .3f) < 1e-6, "Automatic collider did not follow width");

        string before = File.ReadAllText(catalogPath);
        string generated = Snapshot(output);

        foreach (var invalid in new[] { new byte[] { 0, 1, 2 }, Fixture(external: true) })
        {
            File.WriteAllBytes(file, invalid);

            bool rejected = false;

            try
            {
                ImportPipeline.Prepare(assets, output);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Require(rejected, "Invalid GLB was accepted");
            Require(File.ReadAllText(catalogPath) == before && Snapshot(output) == generated, "Failed import changed published assets");
        }

        File.Delete(file);
        ImportPipeline.Prepare(assets, output);
        Require(!File.Exists(Path.Combine(output, "models", id + ".mesh.gz")), "Deleted model remains in output");
        catalog = JsonNode.Parse(File.ReadAllText(catalogPath))!;
        Require(!catalog["routers"]!.AsArray().Any(s => s!["id"]!.GetValue<string>() == id), "Deleted model remains in catalog");
        Console.WriteLine("PASS: GLB transforms, UVs, materials, add/edit/delete and failed-import preservation");
    }

    static byte[] Fixture(bool external = false)
    {
        using var bin = new MemoryStream();

        using (var writer = new BinaryWriter(bin, Encoding.UTF8, leaveOpen: true))
        {
            foreach (float v in new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 })
            {
                writer.Write(v);
            }

            for (int i = 0; i < 9; i++)
            {
                writer.Write(1 / MathF.Sqrt(3));
            }

            foreach (float v in new float[] { .1f, .2f, .3f, .4f, .5f, .6f })
            {
                writer.Write(v);
            }
        }

        var json = JsonNode.Parse("""
        {
          "asset":{"version":"2.0"}, "scene":0, "scenes":[{"nodes":[0]}],
          "nodes":[{"translation":[4,5,6],"children":[1]},{"mesh":0,"scale":[-2,3,0.5]}],
          "buffers":[{"byteLength":96}],
          "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":24}],
          "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,1]},
            {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":3,"type":"VEC2"}],
          "materials":[{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.3,0.4,1],"metallicFactor":0.25,"roughnessFactor":0.6}}],
          "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"material":0}]}]
        }
        """)!;

        if (external)
        {
            json["buffers"]![0]!["uri"] = "missing-external-buffer.bin";
        }

        var bytes = Encoding.UTF8.GetBytes(json.ToJsonString());
        int padded = (bytes.Length + 3) & ~3;
        using var glb = new MemoryStream();

        using (var writer = new BinaryWriter(glb, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x46546C67);
            writer.Write(2);
            writer.Write(12 + 8 + padded + 8 + (int)bin.Length);
            writer.Write(padded);
            writer.Write(0x4E4F534A);
            writer.Write(bytes);

            for (int i = bytes.Length; i < padded; i++)
            {
                writer.Write((byte)' ');
            }

            writer.Write((int)bin.Length);
            writer.Write(0x004E4942);
            writer.Write(bin.ToArray());
        }

        return glb.ToArray();
    }
}
