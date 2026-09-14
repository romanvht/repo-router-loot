using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace RouterLoot;

internal static class Catalog
{
    public static readonly RouterSpec[] All = Read();

    private static RouterSpec[] Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RouterLoot.Catalog.json")
            ?? throw new FileNotFoundException("Router catalog missing");
        using var reader = new StreamReader(stream);
        var data = JsonConvert.DeserializeObject<CatalogData>(reader.ReadToEnd());

        if (data == null || data.routers == null || data.routers.Length == 0
            || data.routers.Any(x => x == null || string.IsNullOrWhiteSpace(x.id) || string.IsNullOrWhiteSpace(x.name))
            || data.routers.Select(x => x.id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.routers.Length
            || data.routers.Select(x => x.PrefabName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.routers.Length)
        {
            throw new InvalidDataException("Expected a nonempty catalog with unique router IDs and names");
        }

        var expected = data.routers.Select(x => $"RouterLoot.Models.{x.id}.mesh.gz").OrderBy(x => x, StringComparer.Ordinal);
        var actual = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(x => x.StartsWith("RouterLoot.Models.", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal);

        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidDataException("Catalog does not match embedded models. Rebuild the mod to regenerate assets from GLB sources.");
        }

        return data.routers;
    }
}

[Serializable]
internal sealed class CatalogData
{
    public RouterSpec[] routers = Array.Empty<RouterSpec>();
}

[Serializable]
internal sealed class RouterSpec
{
    public string id = "";
    public string name = "";
    public float min = 0;
    public float max = 0;
    public float mass = 0;
    public ColliderSpec[] colliders = Array.Empty<ColliderSpec>();

    public string PrefabName => "RouterLoot " + name;
}

[Serializable]
internal sealed class ColliderSpec
{
    public float[] center = Array.Empty<float>();
    public float[] size = Array.Empty<float>();

    public Vector3 Center => new(center[0], center[1], center[2]);
    public Vector3 Size => new(size[0], size[1], size[2]);
}
