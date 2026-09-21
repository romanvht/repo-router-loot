using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using REPOLib;
using REPOLib.Modules;
using REPOLib.Objects.Sdk;
using RouterLoot.Helpers;
using UnityEngine;

namespace RouterLoot;

[BepInPlugin(Id, "Router Loot", "0.1.6")]
[BepInDependency("REPOLib", "4.2.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "romanvht.RouterLoot";

    private readonly List<PrefabRef> registered = new();
    private ConfigEntry<bool> debugSpawn = null!;

    private void Awake()
    {
        gameObject.hideFlags = HideFlags.HideAndDontSave;
        debugSpawn = Config.Bind("Debug", "EnableSpawnKey", false,
            "Host only: press F8 during a level to spawn the router set for testing.");
        BundleLoader.OnAllBundlesLoaded += Initialize;
    }

    private void OnDestroy()
    {
        BundleLoader.OnAllBundlesLoaded -= Initialize;
    }

    private void Initialize()
    {
        BundleLoader.OnAllBundlesLoaded -= Initialize;

        try
        {
            var bundle = AssetBundle.GetAllLoadedAssetBundles().Single(item => item.name == "routerloot.repobundle");
            foreach (var content in bundle.LoadAllAssets<ValuableContent>().OrderBy(item => item.name))
            {
                var prefab = content.Prefab ?? throw new InvalidOperationException($"Missing prefab: {content.name}");
                Settings.Apply(Config, content.name, prefab);
                registered.Add(NetworkPrefabs.PrefabRefs["Valuables/" + prefab.name]);
            }

            Logger.LogInfo($"Router Loot ready: {registered.Count} valuables.");
        }
        catch (Exception error)
        {
            Logger.LogError($"Cannot initialize Router Loot: {error}");
        }
    }

    private void Update()
    {
        if (!DebugSpawn.TryGetCamera(debugSpawn.Value, KeyCode.F8, registered.Count, out var camera))
        {
            return;
        }

        for (int i = 0; i < registered.Count; i++)
        {
            float offset = (i - (registered.Count - 1) / 2f) * 0.65f;
            var position = camera.transform.position + camera.transform.forward * 2f + camera.transform.right * offset;

            Valuables.SpawnValuable(registered[i], position, Quaternion.identity);
        }
    }
}
