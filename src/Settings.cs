using BepInEx.Configuration;
using RouterLoot.Helpers;

namespace RouterLoot;

internal sealed class Settings
{
    public ConfigEntry<float> Min { get; }
    public ConfigEntry<float> Max { get; }
    public ConfigEntry<float> Mass { get; }
    public ConfigEntry<float> Fragility { get; }

    public Settings(ConfigFile config, RouterSpec spec)
    {
        Min = ConfigValues.Bind(config, spec.id, "ValueMin", spec.min, 1, 100000,
            "Minimum base value. Host determines value; game multipliers apply.");
        Max = ConfigValues.Bind(config, spec.id, "ValueMax", spec.max, 1, 100000,
            "Maximum base value. Restart after changes.");
        Mass = ConfigValues.Bind(config, spec.id, "Mass", spec.mass, 0.1f, 30,
            "Mass. Use the same settings on all clients.");
        Fragility = ConfigValues.Bind(config, spec.id, "Fragility", 55f, 0, 100,
            "Impact fragility, 0–100. Use the same settings on all clients.");
    }
}
