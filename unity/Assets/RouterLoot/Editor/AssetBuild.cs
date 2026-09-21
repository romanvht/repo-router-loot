#if ROUTER_LOOT_READY
using System;
using System.IO;
using System.Linq;
using REPOLib.Objects.Sdk;
using REPOLibSdk.Editor;
using UnityEditor;
using UnityEngine;

namespace RouterLoot.Editor
{
    public static class AssetBuild
    {
        public const string BundleName = "routerloot.repobundle";

        [MenuItem("Router Loot/Build Assets", false, 1)]
        public static void Build()
        {
            var mods = AssetDatabase.FindAssets("t:Mod", new[] { "Assets/RouterLoot" });
            if (mods.Length != 1)
            {
                throw new InvalidOperationException("Expected one REPOLib Mod asset in Assets/RouterLoot.");
            }

            string modPath = AssetDatabase.GUIDToAssetPath(mods[0]);
            var mod = AssetDatabase.LoadAssetAtPath<Mod>(modPath);
            var contents = PackageExporter.FindContents(mod).Where(item => !item.IsDependency).Select(item => item.Path).ToArray();
            var valuables = contents.Select(AssetDatabase.LoadAssetAtPath<ValuableContent>).ToArray();
            Validate(valuables);
            var ids = valuables.Select(item => item.name).OrderBy(name => name).ToArray();
            AssetDatabase.SaveAssets();

            string output = Path.GetFullPath("../obj/unity-bundle");
            Directory.CreateDirectory(output);
            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = contents.Append(modPath).ToArray()
            };
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.UseContentHash,
                BuildTarget.StandaloneWindows64);
            if (!manifest)
            {
                throw new InvalidOperationException("AssetBundle build failed.");
            }

            var bundle = AssetBundle.LoadFromFile(Path.Combine(output, BundleName));
            if (!bundle)
            {
                throw new InvalidOperationException("Cannot load the built AssetBundle.");
            }

            try
            {
                var loaded = bundle.LoadAllAssets<ValuableContent>();
                Validate(loaded);
                if (!loaded.Select(item => item.name).OrderBy(name => name).SequenceEqual(ids))
                {
                    throw new InvalidOperationException("Bundle contents do not match the project.");
                }
                Debug.Log($"PASS: {loaded.Length} valuables built and loaded from {BundleName}");
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        public static void Validate(ValuableContent[] valuables)
        {
            if (valuables.Length == 0 || valuables.Any(item => !item || !item.Prefab)
                || valuables.Select(item => item.name).Distinct().Count() != valuables.Length
                || valuables.Select(item => item.Prefab.name).Distinct().Count() != valuables.Length)
            {
                throw new InvalidOperationException("Valuables must have unique IDs, names and assigned prefabs.");
            }

            foreach (var content in valuables)
            {
                var prefab = content.Prefab;
                var body = prefab.GetComponent<Rigidbody>();
                var room = prefab.GetComponent<RoomVolumeCheck>();
                var boxes = prefab.GetComponentsInChildren<BoxCollider>();
                var renderers = prefab.GetComponentsInChildren<MeshRenderer>();
                if (!body || body.isKinematic || !room || !prefab.GetComponent<PhysGrabObject>()
                    || !prefab.transform.Find("Center of Mass") || boxes.Length == 0 || renderers.Length == 0)
                {
                    throw new InvalidDataException($"{content.name}: incomplete valuable prefab.");
                }

                if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(component => !component)
                    || prefab.GetComponentsInChildren<MeshFilter>().Any(filter => !filter.sharedMesh)
                    || renderers.Any(renderer => renderer.sharedMaterials.Any(material => !material || !material.shader)))
                {
                    throw new InvalidDataException($"{content.name}: missing script or material.");
                }

                if (boxes.Any(box => box.isTrigger || !box.enabled || !box.sharedMaterial
                    || !box.GetComponent<PhysGrabObjectCollider>() || !box.GetComponent<PhysGrabObjectBoxCollider>()
                    || box.GetComponent<Renderer>() || box.size.x <= 0 || box.size.y <= 0 || box.size.z <= 0))
                {
                    throw new InvalidDataException($"{content.name}: invalid valuable collider.");
                }

                if (!prefab.valuePreset || prefab.valuePreset.valueMin < 1
                    || prefab.valuePreset.valueMax < prefab.valuePreset.valueMin || !prefab.physAttributePreset
                    || prefab.physAttributePreset.mass <= 0 || !prefab.durabilityPreset || !prefab.audioPreset)
                {
                    throw new InvalidDataException($"{content.name}: invalid valuable presets.");
                }
            }
        }

        [MenuItem("Router Loot/Build Assets", true)]
        public static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
    }
}
#endif
