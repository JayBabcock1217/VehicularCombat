using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// Shared, serializable stat block for a component. Lives in <c>shared</c> so
/// BOTH the client and the headless server can read identical numbers. The client
/// component node [Export]s these and also animates/behaves; the server reads the
/// same values to run the authoritative simulation.
///
/// Design rule (GOALS.md "no rework"): adding a new component kind = a new node
/// + a new entry here. The rig, the spawner, and the network protocol do not
/// change — the rig just sums over whatever components are present.
///
/// NOTE: this MUST live in its own file (filename == class name) so Godot can
/// instantiate it as a script-bound Resource and serialize it as a sub-resource
/// in .tscn/.tres files. It was originally in ComponentKinds.cs, which broke
/// scene loading (Godot cannot bind a Resource whose class name != filename).
/// </summary>
[GlobalClass]
public partial class ComponentStats : Resource
{
    /// <summary>Unique kind of this component (drives how the rig aggregates it).</summary>
    [Export] public ComponentKind Kind { get; set; } = ComponentKind.Unknown;

    /// <summary>Human name for this specific instance (e.g. "Nose Gun", "Port Wing").</summary>
    [Export] public string Label { get; set; } = "";

    /// <summary>
    /// Whether this component is active on this vehicle. A plane can carry a
    /// hardpoint with no weapon mounted; toggling this off excludes it from the
    /// rig's aggregation without deleting the node.
    /// </summary>
    [Export] public bool Enabled { get; set; } = true;

    /// <summary>Mass contributed to the plane, kg. Used by the flight model later.</summary>
    [Export] public float Mass { get; set; } = 0f;

    // --- Per-kind numbers (only the fields relevant to <see cref="Kind"/> are
    // --- read; the rest stay 0). Keeping them flat (one resource) keeps the
    // --- [Export] surface simple and avoids per-kind sub-resource churn.

    // Airfoil:
    [Export] public float Area { get; set; }            // wing area, m^2
    [Export] public float AspectRatio { get; set; }     // span^2 / area
    [Export] public float ClMax { get; set; } = 1.5f;   // max lift coefficient
    [Export] public float CdZero { get; set; } = 0.02f; // parasite drag coefficient
    [Export] public float Oswald { get; set; } = 0.75f; // span-efficiency factor

    // Engine:
    [Export] public float MaxThrust { get; set; }       // N (sea level, static)
    [Export] public float FuelFlowPerSec { get; set; }  // kg/s at full throttle
    [Export] public float FuelCapacity { get; set; }    // kg this engine carries
    [Export] public float SpoolUpSec { get; set; } = 2f;// throttle response time
    [Export] public int Blades { get; set; } = 1;       // prop blades (visual), 0 = jet

    // Landing gear:
    [Export] public int Wheels { get; set; } = 2;
    [Export] public float GearSpeedMax { get; set; } = 70f; // max safe landing speed, m/s
    [Export] public float RetractSec { get; set; } = 3f;    // extend/retract time
    [Export] public bool Retractable { get; set; } = true;

    // Pilot / cockpit:
    [Export] public float Crew { get; set; } = 1;      // seats
    [Export] public float ControlAuthority { get; set; } = 1f; // 0..1 how responsive

    // Radar / sensor:
    [Export] public float DetectRange { get; set; }    // m, max detection range
    [Export] public float ScanRate { get; set; } = 0.5f; // Hz, how fast it sweeps
    [Export] public float ConeFov { get; set; } = 30f;  // deg, beam cone

    // Gun / weapon:
    [Export] public float FireRate { get; set; }       // rounds/sec
    [Export] public float Damage { get; set; }         // per round
    [Export] public float Range { get; set; }          // m, effective range
    [Export] public float BulletSpeed { get; set; }    // m/s
    [Export] public int Ammo { get; set; } = 0;        // 0 = infinite (Stage 0)

    // Missile:
    [Export] public float MissileSpeed { get; set; }   // m/s
    [Export] public float MissileRange { get; set; }   // m, max range
    [Export] public float MissileDamage { get; set; }
    [Export] public int MissileCount { get; set; }     // rounds carried

    // Armor:
    [Export] public float ArmorValue { get; set; }     // flat damage reduction
    [Export] public float ArmorHealth { get; set; }    // HP this plate adds

    /// <summary>Convenience: total HP contributed (armor + structural mass).</summary>
    public float HealthContribution => ArmorHealth + ArmorValue;
}
