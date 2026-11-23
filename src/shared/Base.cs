using Godot;

namespace VehicularCombat.Shared;

/// <summary>
/// The kind of infrastructure a base represents. Different types generate
/// different resource rates (GOALS.md §5.2 / §9 "Infrastructure types").
/// </summary>
public enum InfrastructureType
{
    /// <summary>A spawn base — where players spawn; low generation but strategic.</summary>
    SpawnBase = 0,

    /// <summary>A factory — higher resource generation.</summary>
    Factory = 1,

    /// <summary>A resource site — high generation, no defense of its own beyond AI.</summary>
    ResourceSite = 2,

    /// <summary>An AWACS — provides (placeholder) early-warning; moderate generation.</summary>
    Awacs = 3,

    /// <summary>A missile-defense battery — strong AI defense; low generation.</summary>
    MissileDefense = 4,
}

/// <summary>
/// A base / infrastructure object on the map. It has a faction owner, a hold
/// radius (for capture), and a defense radius (for AI defense). Capture flips
/// ownership and the AI defense allegiance (GOALS.md §5.4).
/// </summary>
public partial class BaseDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string Name { get; set; } = "";
    [Export] public Vector3 Position { get; set; }
    [Export] public InfrastructureType Type { get; set; } = InfrastructureType.SpawnBase;

    // --- Capture / hold tuning --------------------------------------------
    /// <summary>Radius within which a faction must hold to capture.</summary>
    [Export] public float HoldRadius { get; set; } = 40f;
    /// <summary>Seconds of uninterrupted friendly presence required to capture.</summary>
    [Export] public float HoldDuration { get; set; } = 15f;

    // --- Defense tuning (Stage 0 AI defense) --------------------------------
    /// <summary>Radius within which the base's AI defense intercepts hostiles.</summary>
    [Export] public float DefenseRadius { get; set; } = 120f;

    // --- Economy ------------------------------------------------------------
    /// <summary>Resource generation rate this base contributes when friendly-owned.</summary>
    [Export] public float GenerationRate { get; set; } = 5f;

    // Runtime (lives in WorldState, not the def resource).
    /// <summary>Current faction owner id (-1 = unowned/neutral).</summary>
    [Export] public int OwnerFaction { get; set; } = -1;
    /// <summary>Current capture progress (0..1) for the faction currently holding it.</summary>
    [Export] public float CaptureProgress { get; set; } = 0f;
    /// <summary>The faction currently making capture progress (-1 = none).</summary>
    [Export] public int CapturingFaction { get; set; } = -1;
}
