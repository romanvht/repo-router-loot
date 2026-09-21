using BepInEx.Configuration;
using RouterLoot.Helpers;
using UnityEngine;

namespace RouterLoot;

internal static class Settings
{
    public static void Apply(ConfigFile config, string id, ValuableObject prefab)
    {
        var value = Object.Instantiate(prefab.valuePreset);
        var physics = Object.Instantiate(prefab.physAttributePreset);
        var durability = Object.Instantiate(prefab.durabilityPreset);

        value.valueMin = ConfigValues.Bind(config, id, "ValueMin", value.valueMin, 1, 100000,
            "Minimum base value. Host determines value; game multipliers apply.").Value;
        value.valueMax = Mathf.Max(value.valueMin, ConfigValues.Bind(config, id, "ValueMax", value.valueMax, 1, 100000,
            "Maximum base value. Restart after changes.").Value);
        physics.mass = ConfigValues.Bind(config, id, "Mass", physics.mass, 0.1f, 30,
            "Mass. Use the same settings on all clients.").Value;
        durability.fragility = ConfigValues.Bind(config, id, "Fragility", durability.fragility, 0, 100,
            "Impact fragility, 0–100. Use the same settings on all clients.").Value;

        prefab.valuePreset = value;
        prefab.physAttributePreset = physics;
        prefab.durabilityPreset = durability;
        prefab.GetComponent<Rigidbody>().mass = physics.mass;
        prefab.GetComponent<PhysGrabObject>().massOriginal = physics.mass;
    }
}
