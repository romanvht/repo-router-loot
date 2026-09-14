using System.Numerics;
using System.Text.Json;

namespace RouterLoot.Assets;

public sealed class RuntimeModel
{
    public int version { get; set; } = 1;
    public string id { get; set; } = "";
    public float[] size { get; set; } = [];
    public List<RuntimeMaterial> materials { get; set; } = [];
    public List<RuntimePart> parts { get; set; } = [];

    public IEnumerable<Vector3> Points => parts.SelectMany(p => p.positions.Chunk(3).Select(v => new Vector3(v[0], v[1], v[2])));
}

public sealed class RuntimeMaterial
{
    public string name { get; set; } = "";
    public float[] color { get; set; } = [1, 1, 1, 1];
    public float metallic { get; set; }
    public float roughness { get; set; } = .7f;
    public string texture { get; set; } = "";
}

public sealed class RuntimePart
{
    public string name { get; set; } = "";
    public int material { get; set; }
    public List<float> positions { get; set; } = [];
    public List<float> normals { get; set; } = [];
    public List<float> uv { get; set; } = [];
    public List<int> triangles { get; set; } = [];
}

public static class AssetJson
{
    public static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };

    public static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidDataException(message);
        }
    }
}
