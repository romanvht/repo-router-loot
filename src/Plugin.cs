using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib.Modules;
using RouterLoot.Helpers;
using UnityEngine;

namespace RouterLoot;

[BepInPlugin(Id, "Router Loot", "0.1.0")]
[BepInDependency("REPOLib", "4.2.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "romanvht.RouterLoot";

    internal static Plugin Instance = null!;

    internal readonly List<PrefabRef> Registered = new();

    private readonly Dictionary<string, Settings> settings = new();

    private ConfigEntry<bool> debugSpawn = null!;

    private bool initialized;

    /// <summary>Loads router settings and installs the registration hook.</summary>
    private void Awake()
    {
        Instance = this;
        debugSpawn = Config.Bind("Debug", "EnableSpawnKey", false,
            "Host only: press F8 during a level to spawn the router set for testing.");

        foreach (var spec in Catalog.All)
        {
            settings.Add(spec.Id, new Settings(Config, spec));
        }

        new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
        Logger.LogInfo("Router Loot loaded. Models are embedded; Unity Editor is not required.");
    }

    /// <summary>Builds and registers every catalog model once.</summary>
    internal void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;

        try
        {
            var donor = ValuablePrefab.LoadDonor();
            var collider = StockCollider.FindTemplate(donor);
            var material = MaterialFactory.FindTemplate(donor);
            var storage = ValuablePrefab.CreateStorage("RouterLoot Prefabs");

            foreach (var spec in Catalog.All)
            {
                try
                {
                    RegisterRouter(spec, donor, storage.transform, collider, material);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Cannot register {spec.Id}: {ex}");
                }
            }

            Logger.LogInfo($"Router Loot ready: {Registered.Count}/{Catalog.All.Length} valuables registered.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    /// <summary>Creates one router with its model, colliders and value settings.</summary>
    private void RegisterRouter(RouterSpec spec, GameObject donor, Transform storage,
        BoxCollider collider, Material template)
    {
        var model = ModelData.Load(spec.Id);
        using var prefab = new ValuablePrefab(donor, storage, spec.PrefabName);

        model.Attach(prefab, template);

        foreach (var box in spec.Colliders)
        {
            StockCollider.Attach(collider, prefab.Root.transform, "Router Collider",
                box.Center, box.Size, Quaternion.Euler(box.Rotation));
        }

        var config = settings[spec.Id];

        prefab.Configure(config.Min.Value, config.Max.Value, config.Mass.Value,
            config.Fragility.Value, spec.Colliders[0].Center);
        prefab.Root.AddComponent<RouterMarker>().ModelId = spec.Id;
        Registered.Add(prefab.Register());
        Logger.LogInfo($"Registered {prefab.Root.name}: {model.parts.Sum(p => p.triangles.Length / 3)} triangles, {spec.Colliders.Length} colliders.");
    }

    /// <summary>Spawns the router set when the host presses F8.</summary>
    private void Update()
    {
        if (!DebugSpawn.TryGetCamera(debugSpawn.Value, KeyCode.F8, Registered.Count, out var camera))
        {
            return;
        }

        for (int i = 0; i < Registered.Count; i++)
        {
            float offset = (i - (Registered.Count - 1) / 2f) * 0.65f;
            var position = camera.transform.position + camera.transform.forward * 2f + camera.transform.right * offset;

            Valuables.SpawnValuable(Registered[i], position, Quaternion.identity);
        }
    }
}

public sealed class RouterMarker : MonoBehaviour
{
    public string ModelId = "";
}

[HarmonyPatch(typeof(RunManager), "Awake")]
internal static class RunManagerPatch
{
    /// <summary>Registers routers after REPOLib is ready.</summary>
    [HarmonyPostfix, HarmonyAfter("REPOLib")]
    private static void Postfix() => Plugin.Instance.Initialize();
}
