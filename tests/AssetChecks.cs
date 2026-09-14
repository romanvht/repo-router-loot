using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using RouterLoot.Assets;

static class AssetChecks
{
    static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    static string DefaultAssets([CallerFilePath] string source = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../assets"));

    static double[] Numbers(JsonElement element, string key, int? length = null)
    {
        var values = element.GetProperty(key).EnumerateArray().Select(x => x.GetDouble()).ToArray();

        Require((length == null || values.Length == length) && values.All(double.IsFinite), $"Invalid {key}");

        return values;
    }

    static void Check(string assets)
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "catalog.json")));
        var specs = catalog.RootElement.GetProperty("routers").EnumerateArray().ToArray();
        var files = Directory.GetFiles(Path.Combine(assets, "models"))
            .Where(p => p.EndsWith(".mesh.gz", StringComparison.OrdinalIgnoreCase)).ToArray();
        var ids = specs.Select(s => s.GetProperty("id").GetString() ?? "").ToArray();

        Require(ids.Length > 0 && ids.All(id => Regex.IsMatch(id, @"\A[a-zA-Z0-9_-]+\z")), "Invalid or empty catalog IDs");
        Require(ids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == ids.Length, "Duplicate catalog ID");
        Require(files.All(p => p.EndsWith(".mesh.gz", StringComparison.Ordinal)), "Use lowercase .mesh.gz extension");

        var fileIds = files.Select(p => Path.GetFileName(p)[..^8]).ToArray();

        Require(ids.Order(StringComparer.Ordinal).SequenceEqual(fileIds.Order(StringComparer.Ordinal)), "Catalog IDs do not match model files");

        var names = specs.Select(s => s.GetProperty("name").GetString()).ToArray();

        Require(names.All(n => !string.IsNullOrWhiteSpace(n)) && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length,
            "Display names must be nonempty and unique");

        foreach (var spec in specs)
        {
            var id = spec.GetProperty("id").GetString()!;

            try
            {
                var min = spec.GetProperty("min").GetDouble();
                var max = spec.GetProperty("max").GetDouble();
                var mass = spec.GetProperty("mass").GetDouble();

                Require(double.IsFinite(min) && double.IsFinite(max) && double.IsFinite(mass)
                    && min >= 1 && min <= max && max <= 100000 && mass >= .1 && mass <= 30, "Invalid price or mass");

                var boxes = spec.GetProperty("colliders").EnumerateArray()
                    .Select(b => (Center: Numbers(b, "center", 3), Size: Numbers(b, "size", 3))).ToArray();

                Require(boxes.Length > 0 && boxes.All(b => b.Size.All(x => x > 0)), "Invalid colliders");

                using var file = File.OpenRead(Path.Combine(assets, "models", id + ".mesh.gz"));
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var model = JsonDocument.Parse(gzip);
                var data = model.RootElement;

                Require(data.GetProperty("version").GetInt32() == 1 && data.GetProperty("id").GetString() == id, "Invalid model header");

                var size = Numbers(data, "size", 3);

                Require(size.All(x => x > 0 && x <= 2), "Invalid model dimensions");

                var materials = data.GetProperty("materials").GetArrayLength();
                var parts = data.GetProperty("parts").EnumerateArray().ToArray();

                Require(materials > 0 && parts.Length > 0, "Empty model");

                var lo = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
                var hi = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };

                foreach (var part in parts)
                {
                    var positions = Numbers(part, "positions");
                    int vertices = positions.Length / 3;

                    Require(positions.Length % 3 == 0 && vertices > 0 && vertices <= 150000, "Invalid vertices");
                    Numbers(part, "normals", positions.Length);
                    Numbers(part, "uv", vertices * 2);

                    var triangles = part.GetProperty("triangles").EnumerateArray().Select(x => x.GetInt32()).ToArray();

                    Require(triangles.Length > 0 && triangles.Length % 3 == 0
                        && triangles.All(i => i >= 0 && i < vertices), "Invalid triangle indices");

                    int material = part.GetProperty("material").GetInt32();

                    Require(material >= 0 && material < materials, "Invalid material reference");

                    for (int v = 0; v < positions.Length; v += 3)
                    {
                        for (int axis = 0; axis < 3; axis++)
                        {
                            lo[axis] = Math.Min(lo[axis], positions[v + axis]);
                            hi[axis] = Math.Max(hi[axis], positions[v + axis]);
                        }

                        Require(boxes.Any(b => Enumerable.Range(0, 3).All(axis =>
                            Math.Abs(positions[v + axis] - b.Center[axis]) <= b.Size[axis] / 2 + .015)), "Colliders do not cover the model");
                    }
                }

                Require(Enumerable.Range(0, 3).All(axis => Math.Abs(hi[axis] - lo[axis] - size[axis]) <= .015), "Dimensions differ from mesh bounds");
                Require(Math.Abs(lo[1]) <= .015 && Math.Abs(lo[0] + hi[0]) <= .03 && Math.Abs(lo[2] + hi[2]) <= .03,
                    "Center X/Z and put the model bottom at Y=0");
                Console.WriteLine($"PASS {id}: catalog, mesh and colliders");
            }
            catch (Exception error)
            {
                throw new InvalidDataException($"{id}: {error.Message}", error);
            }
        }

        Console.WriteLine($"PASS: {ids.Length} ready router models");
    }

    static int Main(string[] args)
    {
        try
        {
            Require(args.Length <= 1, "Usage: AssetChecks [assets-directory]");

            string source = args.Length == 1 ? Path.GetFullPath(args[0]) : DefaultAssets();
            string scratch = Path.Combine(Path.GetTempPath(), "RouterLootChecks-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path.Combine(scratch, "assets", "models"));

            try
            {
                string assets = Path.Combine(scratch, "assets");
                string output = Path.Combine(scratch, "generated");

                foreach (var file in Directory.GetFiles(Path.Combine(source, "models"), "*.glb"))
                {
                    File.Copy(file, Path.Combine(assets, "models", Path.GetFileName(file)));
                }

                File.Copy(Path.Combine(source, "catalog.json"), Path.Combine(assets, "catalog.json"));

                string original = File.ReadAllText(Path.Combine(assets, "catalog.json"));

                ImportPipeline.Prepare(assets, output);
                Require(File.ReadAllText(Path.Combine(assets, "catalog.json")) == original, "Existing catalog settings changed");
                Check(output);
                PipelineChecks.Run(assets, output);
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }

            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: {error.Message}");

            return 1;
        }
    }
}
