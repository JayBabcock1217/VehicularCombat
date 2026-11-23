using Godot;

namespace VehicularCombat.Shared;

/// <summary>
/// A faction (one of the two opposing sides). Carries identity + its shared
/// resource pool. The resource pool is the team economy: it generates over time
/// from captured/built infrastructure and funds vehicle spawning (GOALS.md §5.2).
/// </summary>
public partial class FactionDef : Resource
{
    [Export] public int Id { get; set; }
    [Export] public string Name { get; set; } = "";
    [Export] public Color Color { get; set; } = Colors.White;

    /// <summary>Base (flat) resource generation rate, units/sec.</summary>
    [Export] public float BaseGenerationRate { get; set; } = 5f;

    // Runtime (not serialized to the def resource; lives in WorldState).
    /// <summary>Current shared resource pool balance.</summary>
    [Export] public float ResourcePool { get; set; } = 0f;
}

/// <summary>
/// The two-faction set. Stage 0 is fixed at two (GOALS.md §5.2). Id 0 and 1.
/// </summary>
public static class Factions
{
    public const int Alpha = 0;
    public const int Bravo = 1;
    public const int Count = 2;

    public static FactionDef CreateDefault(int id)
    {
        var f = new FactionDef { Id = id };
        f.Name = id == Alpha ? "Alpha" : "Bravo";
        f.Color = id == Alpha ? new Color(0.2f, 0.5f, 1f) : new Color(1f, 0.35f, 0.2f);
        f.BaseGenerationRate = 5f;
        f.ResourcePool = 1000f; // starting pool so the world is playable immediately
        return f;
    }
}
