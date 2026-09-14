using BepInEx.Configuration;
using RouterLoot.Helpers;

namespace RouterLoot;

internal sealed class Settings
{
    public ConfigEntry<float> Min { get; }

    public ConfigEntry<float> Max { get; }

    public ConfigEntry<float> Mass { get; }

    public ConfigEntry<float> Fragility { get; }

    /// <summary>Binds the existing configuration keys for one router model.</summary>
    public Settings(ConfigFile config, RouterSpec spec)
    {
        Min = ConfigValues.Bind(config, spec.Id, "ValueMin", spec.Min, 1, 100000,
            "Minimum base value. Host determines value; game multipliers apply.");
        Max = ConfigValues.Bind(config, spec.Id, "ValueMax", spec.Max, 1, 100000,
            "Maximum base value. Restart after changes.");
        Mass = ConfigValues.Bind(config, spec.Id, "Mass", spec.Mass, 0.1f, 30,
            "Mass. Use the same settings on all clients.");
        Fragility = ConfigValues.Bind(config, spec.Id, "Fragility", 55f, 0, 100,
            "Impact fragility, 0–100. Use the same settings on all clients.");
    }
}
