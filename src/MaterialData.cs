using System;
using System.IO;
using System.Linq;
using RouterLoot.Helpers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RouterLoot;

[Serializable]
internal sealed class MaterialData
{
    public string name = "";
    public string texture = "";
    public float[] color = { 1f, 1f, 1f, 1f };
    public float metallic = 0;
    public float roughness = 0.7f;

    public Material Create(Material template)
    {
        if (color.Length != 4 || color.Any(x => !ModelData.Finite(x)))
        {
            throw new InvalidDataException("Invalid material color.");
        }

        var tint = new Color(color[0], color[1], color[2], color[3]).gamma;
        var material = MaterialFactory.Create(template, "RouterLoot/" + name, tint, metallic, 1f - Mathf.Clamp01(roughness));
        Texture2D? image = null;

        try
        {
            if (!string.IsNullOrEmpty(texture))
            {
                image = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                {
                    name = material.name,
                    filterMode = FilterMode.Bilinear
                };

                if (!ImageConversion.LoadImage(image, Convert.FromBase64String(texture), true))
                {
                    throw new InvalidDataException("Texture decode failed.");
                }

                material.mainTexture = image;
            }

            return material;
        }
        catch
        {
            if (image)
            {
                Object.Destroy(image);
            }

            Object.Destroy(material);

            throw;
        }
    }
}
