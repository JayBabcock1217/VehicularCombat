using Godot;
using System.Collections.Generic;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// The ROOT node of a vehicle scene. You build each plane type as a scene whose
/// root is a <see cref="VehicleRig"/> (or a subclass like <c>AirplaneRig</c>);
/// under it you drop:
///   1. YOUR MODEL — a Node3D/MeshInstance3D (or an imported .glb/.blend) child.
///   2. COMPONENTS — any number of <see cref="VehicleComponent"/> nodes, each
///      positioned on the mesh and carrying [Export]ed stats.
///
/// The rig does the work of TIEING IT TOGETHER:
///   * On ready it walks its children, finds every <see cref="VehicleComponent"/>,
///     registers them, and aggregates their <see cref="ComponentStats"/> into the
///     plane's overall capabilities (total thrust, total HP, weapons list, etc.).
///   * It exposes an <see cref="Aggregate"/> snapshot the rest of the game reads.
///   * It is the ANCHOR the camera and the flight model attach to.
///
/// Client vs server: the SAME rig node type is used on both sides. The server
/// (headless) uses it to read the aggregated stats for the authoritative sim; the
/// client also runs the components' visual/behavior hooks. Adding a new component
/// kind requires NO change here — the rig just sums over whatever is present
/// (GOALS.md "no rework").
/// </summary>
public partial class VehicleRig : Node3D
{
    /// <summary>
    /// All components found under this rig (discovered on ready). The rig is the
    /// single source of truth for "what can this vehicle do".
    /// </summary>
    public IReadOnlyList<VehicleComponent> Components { get; private set; } = new List<VehicleComponent>();

    /// <summary>
    /// The aggregated capabilities of this vehicle, recomputed from
    /// <see cref="Components"/>. Cheap to read; the game reads this, not the
    /// individual components.
    /// </summary>
    public RigAggregate Aggregate { get; private set; } = new();

    /// <summary>
    /// The node the model lives under. By default the model is just another child
    /// of the rig; if you wrap your imported model in a child named "Model", the
    /// rig uses that as the visual anchor (so components can be parented relative
    /// to the model). Optional — leave null to use the rig origin.
    /// </summary>
    public Node3D? ModelRoot { get; private set; }

    public override void _Ready()
    {
        CollectComponents();
        Aggregate = AggregateCapabilities();

        // Wire each component to its host (so components can query the rig).
        foreach (var c in Components) c.AttachToHost(this);

        // Client-side: let each component build its own visual + start behavior.
        // The headless server (no rendering) skips these — it only reads Stats.
        if (Vehicles.IsClient)
        {
            foreach (var c in Components) c.OnClientReady();
        }

        GD.Print($"VehicleRig '{Name}': {Components.Count} component(s) — {DescribeAggregate()}");
    }

    public override void _ExitTree()
    {
        if (Vehicles.IsClient)
        {
            foreach (var c in Components) c.OnClientExitTree();
        }
    }

    public override void _Process(double delta)
    {
        // Only the client animates components (server is headless).
        if (Vehicles.IsClient)
        {
            foreach (var c in Components) c.OnClientProcess(delta);
        }
    }

    /// <summary>
    /// Find every VehicleComponent under this rig (recursively), so components can
    /// be nested under your model node or in intermediate group nodes. Order is
    /// tree order (stable), which keeps the aggregate deterministic.
    /// </summary>
    private void CollectComponents()
    {
        var found = new List<VehicleComponent>();
        CollectRecursive(this, found);
        Components = found;

        // Optional explicit model anchor: a direct child named "Model".
        var model = GetNodeOrNull<Node3D>("Model");
        if (model != null) ModelRoot = model;
    }

    private static void CollectRecursive(Node node, List<VehicleComponent> outList)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is VehicleComponent vc) outList.Add(vc);
            if (child is Node childNode) CollectRecursive(childNode, outList);
        }
    }

    /// <summary>
    /// Recompute the aggregated capabilities from the current components. Call this
    /// if you add/remove components at runtime (e.g. a gear deploy, a weapon swap).
    /// </summary>
    public void Recompute()
    {
        CollectComponents();
        Aggregate = AggregateCapabilities();
        foreach (var c in Components) c.AttachToHost(this);
    }

    /// <summary>
    /// Sum the components' stats into a single capability snapshot. This is the
    /// "what can this plane do" answer the flight model, HUD, and server read.
    /// </summary>
    private RigAggregate AggregateCapabilities()
    {
        var agg = new RigAggregate();
        foreach (var c in Components)
        {
            if (!c.Stats.Enabled) continue;
            var s = c.Stats;
            agg.Mass += s.Mass;
            switch (s.Kind)
            {
                case ComponentKind.Airfoil:
                    agg.WingArea += s.Area;
                    agg.CdZero += s.CdZero;
                    agg.ClMax = Mathf.Max(agg.ClMax, s.ClMax);
                    agg.AspectRatio = s.AspectRatio;
                    break;
                case ComponentKind.Engine:
                    agg.MaxThrust += s.MaxThrust;
                    agg.FuelFlow += s.FuelFlowPerSec;
                    agg.FuelCapacity += s.FuelCapacity;
                    agg.EngineCount++;
                    agg.Blades = Mathf.Max(agg.Blades, s.Blades);
                    agg.SpoolUpSec = Mathf.Max(agg.SpoolUpSec, s.SpoolUpSec);
                    break;
                case ComponentKind.Rotor:
                    agg.HasRotor = true;
                    agg.MaxThrust += s.MaxThrust;
                    agg.RotorBlades += s.Blades;
                    break;
                case ComponentKind.LandingGear:
                    agg.HasLandingGear = true;
                    agg.GearWheels += s.Wheels;
                    agg.GearSpeedMax = Mathf.Min(agg.GearSpeedMax > 0 ? agg.GearSpeedMax : s.GearSpeedMax, s.GearSpeedMax);
                    agg.GearRetractable &= s.Retractable;
                    agg.GearRetractSec = Mathf.Max(agg.GearRetractSec, s.RetractSec);
                    break;
                case ComponentKind.Pilot:
                    agg.HasPilot = true;
                    agg.Crew = Mathf.Max(agg.Crew, s.Crew);
                    agg.ControlAuthority = Mathf.Max(agg.ControlAuthority, s.ControlAuthority);
                    break;
                case ComponentKind.Radar:
                    agg.HasRadar = true;
                    agg.DetectRange = Mathf.Max(agg.DetectRange, s.DetectRange);
                    agg.ScanRate = Mathf.Max(agg.ScanRate, s.ScanRate);
                    agg.ConeFov = Mathf.Max(agg.ConeFov, s.ConeFov);
                    break;
                case ComponentKind.Gun:
                    agg.Weapons.Add(new WeaponSpec
                    {
                        Name = string.IsNullOrEmpty(s.Label) ? "Gun" : s.Label,
                        FireRate = s.FireRate,
                        Damage = s.Damage,
                        Range = s.Range,
                        BulletSpeed = s.BulletSpeed,
                        Ammo = s.Ammo,
                        Mount = c.GlobalPosition,
                        MountLocal = c.Position,
                    });
                    agg.AmmoTotal += s.Ammo;
                    break;
                case ComponentKind.Missile:
                    agg.Weapons.Add(new WeaponSpec
                    {
                        Name = string.IsNullOrEmpty(s.Label) ? "Missile" : s.Label,
                        MissileSpeed = s.MissileSpeed,
                        MissileRange = s.MissileRange,
                        Damage = s.MissileDamage,
                        Ammo = s.MissileCount,
                        Mount = c.GlobalPosition,
                        MountLocal = c.Position,
                        IsMissile = true,
                    });
                    break;
                case ComponentKind.Hardpoint:
                    agg.Hardpoints.Add(c.GlobalPosition);
                    break;
                case ComponentKind.Armor:
                    agg.ArmorValue += s.ArmorValue;
                    agg.Health += s.HealthContribution;
                    break;
                case ComponentKind.Anchor:
                    agg.Anchors[s.Label] = c.GlobalPosition;
                    break;
            }
        }
        // A plane with no armor still has structural HP from its mass (arbitrary
        // Stage 0 baseline: 1 HP per kg, floor 100).
        if (agg.Health <= 0f) agg.Health = Mathf.Max(100f, agg.Mass);
        return agg;
    }

    private string DescribeAggregate() =>
        $"thrust={Aggregate.MaxThrust:F0}N, mass={Aggregate.Mass:F0}kg, hp={Aggregate.Health:F0}, " +
        $"guns={Aggregate.Weapons.Count}, hardpoints={Aggregate.Hardpoints.Count}, " +
        $"radar={Aggregate.HasRadar}, gear={Aggregate.HasLandingGear}";

    /// <summary>Find the first component of a given kind, or null.</summary>
    public VehicleComponent? FindComponent(ComponentKind kind)
    {
        foreach (var c in Components)
            if (c.Stats.Kind == kind) return c;
        return null;
    }

    /// <summary>All components of a given kind.</summary>
    public List<VehicleComponent> FindComponents(ComponentKind kind)
    {
        var list = new List<VehicleComponent>();
        foreach (var c in Components)
            if (c.Stats.Kind == kind) list.Add(c);
        return list;
    }
}

/// <summary>
/// Aggregated "what can this vehicle do" snapshot, computed by
/// <see cref="VehicleRig"/>. The flight model, HUD, and server read this instead
/// of poking individual components.
/// </summary>
public partial class RigAggregate
{
    // Structural / mass:
    public float Mass;
    public float Health;
    public float ArmorValue;

    // Aerodynamics (summed over airfoils):
    public float WingArea;
    public float AspectRatio;
    public float ClMax;
    public float CdZero;

    // Power (summed over engines/rotors):
    public float MaxThrust;
    public float FuelFlow;
    public float FuelCapacity;
    public int EngineCount;
    public int Blades;        // max prop blades (visual spin speed)
    public float SpoolUpSec;
    public bool HasRotor;
    public int RotorBlades;

    // Landing gear:
    public bool HasLandingGear;
    public bool GearRetractable = true;
    public int GearWheels;
    public float GearSpeedMax;
    public float GearRetractSec;

    // Crew:
    public bool HasPilot;
    public float Crew;
    public float ControlAuthority;

    // Sensors:
    public bool HasRadar;
    public float DetectRange;
    public float ScanRate;
    public float ConeFov;

    // Weapons + stores:
    public readonly List<WeaponSpec> Weapons = new();
    public int AmmoTotal;
    public readonly List<Vector3> Hardpoints = new();
    public readonly Dictionary<string, Vector3> Anchors = new();
}

/// <summary>A single weapon/launcher spec aggregated from a gun/missile component.</summary>
public partial class WeaponSpec
{
    public string Name = "";
    public bool IsMissile;
    public float FireRate;
    public float Damage;
    public float Range;
    public float BulletSpeed;
    public float MissileSpeed;
    public float MissileRange;
    public int Ammo;
    public Vector3 Mount;        // world position of the mount (set at aggregate time)
    public Vector3 MountLocal;   // rig-local position (stable across frames)
}
