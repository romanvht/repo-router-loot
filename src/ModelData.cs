using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using RouterLoot.Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace RouterLoot;

[Serializable]
internal sealed class ModelData
{
    public int version = 0;

    public string id = "";

    public float[] size = Array.Empty<float>();

    public MaterialData[] materials = Array.Empty<MaterialData>();

    public MeshData[] parts = Array.Empty<MeshData>();

    /// <summary>Loads and validates an embedded compressed model.</summary>
    public static ModelData Load(string id)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"RouterLoot.Models.{id}.mesh.gz")
            ?? throw new FileNotFoundException($"Embedded model missing: {id}");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var data = JsonConvert.DeserializeObject<ModelData>(reader.ReadToEnd());

        if (data == null || data.version != 1 || data.id != id || data.parts.Length == 0 || data.materials.Length == 0)
        {
            throw new InvalidDataException($"Invalid mesh header: {id}");
        }

        if (data.size.Length != 3 || data.size.Any(x => !Finite(x) || x <= 0 || x > 2))
        {
            throw new InvalidDataException($"Invalid model dimensions: {id}");
        }

        foreach (var part in data.parts)
        {
            part.Validate(data.materials.Length);
        }

        return data;
    }

    /// <summary>Checks that a serialized coordinate is finite.</summary>
    internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>Attaches every model part and tracks its allocated resources.</summary>
    public void Attach(ValuablePrefab prefab, Material template)
    {
        var createdMaterials = new Material[materials.Length];

        for (int i = 0; i < materials.Length; i++)
        {
            var material = prefab.Own(materials[i].Create(template));

            if (material.mainTexture)
            {
                prefab.Own(material.mainTexture);
            }

            createdMaterials[i] = material;
        }

        foreach (var part in parts)
        {
            var mesh = prefab.Own(new Mesh
            {
                name = $"RouterLoot/{id}/{part.name}",
                indexFormat = IndexFormat.UInt32
            });

            mesh.vertices = Triples(part.positions);
            mesh.normals = Triples(part.normals);
            mesh.uv = Pairs(part.uv);
            mesh.triangles = part.triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            prefab.AddVisual(part.name, mesh, createdMaterials[part.material]);
        }
    }

    /// <summary>Converts packed coordinates to three-dimensional vectors.</summary>
    internal static Vector3[] Triples(float[] values)
    {
        var result = new Vector3[values.Length / 3];

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new Vector3(values[i * 3], values[i * 3 + 1], values[i * 3 + 2]);
        }

        return result;
    }

    /// <summary>Converts packed texture coordinates to two-dimensional vectors.</summary>
    private static Vector2[] Pairs(float[] values)
    {
        var result = new Vector2[values.Length / 2];

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new Vector2(values[i * 2], values[i * 2 + 1]);
        }

        return result;
    }
}

[Serializable]
internal sealed class MeshData
{
    public string name = "";

    public int material = 0;

    public float[] positions = Array.Empty<float>();

    public float[] normals = Array.Empty<float>();

    public float[] uv = Array.Empty<float>();

    public int[] triangles = Array.Empty<int>();

    /// <summary>Validates mesh arrays and material references before allocation.</summary>
    public void Validate(int materialCount)
    {
        int count = positions.Length / 3;

        if (count == 0 || count > 150000 || positions.Length % 3 != 0 || normals.Length != positions.Length
            || uv.Length != count * 2 || triangles.Length == 0 || triangles.Length % 3 != 0
            || material < 0 || material >= materialCount || triangles.Any(i => i < 0 || i >= count)
            || positions.Any(x => !ModelData.Finite(x)) || normals.Any(x => !ModelData.Finite(x))
            || uv.Any(x => !ModelData.Finite(x)))
        {
            throw new InvalidDataException($"Invalid mesh arrays: {name}");
        }
    }
}
