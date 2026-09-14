using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static RouterLoot.Assets.AssetJson;

namespace RouterLoot.Assets;

public static class ImportPipeline
{
    public static void Prepare(string assets, string output)
    {
        assets = Path.GetFullPath(assets);
        output = Path.GetFullPath(output);
        Require(!string.Equals(assets.TrimEnd(Path.DirectorySeparatorChar), output.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
            "Generated output must be separate from source assets");

        var files = Directory.GetFiles(Path.Combine(assets, "models"), "*")
            .Where(p => p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();

        Require(files.Length > 0, "Add self-contained .glb models to assets/models");

        var ids = files.Select(Path.GetFileNameWithoutExtension).Select(s => s!).ToArray();

        Require(files.All(p => p.EndsWith(".glb", StringComparison.Ordinal)) && ids.All(id => Regex.IsMatch(id, @"\A[a-zA-Z0-9_-]+\z")),
            "Use unique ASCII model IDs and lowercase .glb extensions");
        Require(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Length, "Duplicate model ID");

        var catalogPath = Path.Combine(assets, "catalog.json");
        var original = File.Exists(catalogPath) ? JsonNode.Parse(File.ReadAllText(catalogPath))!.AsObject() : new JsonObject { ["routers"] = new JsonArray() };
        var document = original.DeepClone().AsObject();
        var old = document["routers"]!.AsArray();
        var existing = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in old)
        {
            var spec = item!.AsObject();
            var id = spec["id"]!.GetValue<string>();

            Require(existing.TryAdd(id, spec), "Duplicate catalog ID: " + id);
        }

        var models = new Dictionary<string, RuntimeModel>(StringComparer.Ordinal);
        var entries = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        for (int i = 0; i < files.Length; i++)
        {
            string id = ids[i];

            try
            {
                existing.TryGetValue(id, out var spec);

                if (spec != null)
                {
                    Require(spec["id"]!.GetValue<string>() == id, "Catalog ID case differs from filename");
                }

                float width = spec?["width"]?.Deserialize<float>() ?? .42f;
                var model = GlbImporter.Load(files[i], width);

                spec = spec?.DeepClone().AsObject() ?? new JsonObject
                {
                    ["id"] = id,
                    ["name"] = id,
                    ["min"] = 500,
                    ["max"] = 1000,
                    ["mass"] = 1,
                    ["width"] = width,
                    ["autoColliders"] = true
                };

                if (spec["autoColliders"]?.GetValue<bool>() == true)
                {
                    spec["colliders"] = JsonSerializer.SerializeToNode(new[] { new
                    {
                        center = new[] { 0f, model.size[1] / 2, 0f },
                        size = model.size.Select(x => Math.Max(x, .014f)).ToArray()
                    }});
                }

                ValidateSpec(spec, model);
                models.Add(id, model);
                entries.Add(id, spec);
                Console.WriteLine($"READY {id}: {model.parts.Sum(p => p.triangles.Count / 3)} triangles, width {model.size[0]:0.###} m");
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"{id}: {error.Message}", error);
            }
        }

        var names = entries.Values.Select(s => s["name"]!.GetValue<string>()).ToArray();

        Require(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length, "Duplicate display names");

        var order = old.Select(s => s!["id"]!.GetValue<string>()).Where(entries.ContainsKey)
            .Concat(ids.Where(id => !existing.ContainsKey(id))).ToArray();

        document["routers"] = new JsonArray(order.Select(id => (JsonNode)entries[id]).ToArray());

        var pretty = new JsonSerializerOptions { WriteIndented = true };
        string catalog = document.ToJsonString(pretty) + "\n";
        var payloads = models.ToDictionary(pair => pair.Key, pair => Compress(pair.Value));
        var modelOutput = Path.Combine(output, "models");

        Directory.CreateDirectory(modelOutput);

        foreach (var pair in payloads)
        {
            WriteIfChanged(Path.Combine(modelOutput, pair.Key + ".mesh.gz"), pair.Value);
        }

        foreach (var obsolete in Directory.GetFiles(modelOutput, "*.mesh.gz"))
        {
            if (!models.ContainsKey(Path.GetFileName(obsolete)[..^8]))
            {
                File.Delete(obsolete);
            }
        }

        WriteIfChanged(Path.Combine(output, "catalog.json"), System.Text.Encoding.UTF8.GetBytes(catalog));

        if (!JsonNode.DeepEquals(original, document))
        {
            WriteIfChanged(catalogPath, System.Text.Encoding.UTF8.GetBytes(catalog));
        }
    }

    static byte[] Compress(RuntimeModel model)
    {
        using var bytes = new MemoryStream();

        using (var gzip = new GZipStream(bytes, CompressionLevel.Optimal, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, model, Options);
        }

        return bytes.ToArray();
    }

    static void WriteIfChanged(string path, byte[] content)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        {
            return;
        }

        string temp = path + ".tmp";

        try
        {
            File.WriteAllBytes(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    static void ValidateSpec(JsonObject spec, RuntimeModel model)
    {
        Require(!string.IsNullOrWhiteSpace(spec["name"]?.GetValue<string>()), "Name is empty");

        float min = spec["min"]!.Deserialize<float>();
        float max = spec["max"]!.Deserialize<float>();
        float mass = spec["mass"]!.Deserialize<float>();

        Require(float.IsFinite(min) && float.IsFinite(max) && float.IsFinite(mass)
            && min >= 1 && min <= max && max <= 100000 && mass >= .1f && mass <= 30, "Invalid price/mass");

        var boxes = spec["colliders"]?.AsArray().Select(b =>
            (center: b!["center"]!.Deserialize<float[]>()!, size: b["size"]!.Deserialize<float[]>()!)).ToArray();

        Require(boxes != null && boxes.Length > 0, "Colliders missing; set autoColliders to true for an automatic box");
        Require(boxes.All(b => b.center.Length == 3 && b.size.Length == 3 && b.center.All(float.IsFinite)
            && b.size.All(x => float.IsFinite(x) && x > 0)), "Invalid collider coordinates");
        Require(model.Points.All(p => boxes.Any(b => Math.Abs(p.X - b.center[0]) <= b.size[0] / 2 + .015
            && Math.Abs(p.Y - b.center[1]) <= b.size[1] / 2 + .015 && Math.Abs(p.Z - b.center[2]) <= b.size[2] / 2 + .015)),
            "Custom colliders no longer cover the model; adjust them or set autoColliders to true");
    }
}
