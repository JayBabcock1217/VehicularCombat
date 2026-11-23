using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// Base class for every drop-in vehicle component. A component is a Node3D you
/// place UNDER the vehicle rig (e.g. <c>AirplaneRig</c>), position on the model
/// (engine at the wing root, gun in the nose), and tune with [Export]ed stats.
///
/// WHY a single base class:
///  * It is a Node3D so it has a transform — you can put it anywhere on the mesh.
///  * It carries the shared <see cref="Stats"/> so BOTH the client (behavior +
///    visual) and the headless server (authoritative sim) read the SAME numbers.
///  * The rig discovers components by walking its children and collecting their
///    <see cref="Stats"/> — so adding a new component kind needs NO rig change.
///
/// Client vs server: the SAME class is used on both sides. On the client it also
/// animates/behaves (spinning prop, muzzle flash, radar sweep). On the headless
/// server the visual/behavior hooks are simply never called — the server only
/// reads <see cref="Stats"/>. Subclasses override the (virtual) behavior hooks.
///
/// Orientation convention (matches the rest of the game): the vehicle's heading
/// (forward/nose) is +X, up is +Y, and +Z is the vehicle's LEFT side. A
/// component's local transform is relative to the rig, so a gun placed at
/// (10, 0, 0) sits 10 units in front of the center, pointing +X.
/// </summary>
public partial class VehicleComponent : Node3D
{
    /// <summary>
    /// A short label for this instance (e.g. "Nose Gear", "Wing Missile"). Shown
    /// in the scene tree / HUD and used to name anchor points. Setting this also
    /// updates <see cref="Stats"/>.Label so the server reads it too.
    /// </summary>
    [Export] public string Label
    {
        get => Stats.Label;
        set => Stats.Label = value ?? "";
    }

    /// <summary>
    /// The component's tunable stats. Embedded as an [Export] sub-resource so you
    /// edit it in the Inspector under "Stats". The server reads this directly; the
    /// client reads it for both behavior and to drive its visual.
    ///
    /// If a component is placed with empty Stats (the common case when you just
    /// drop the node), <see cref="EnsureDefaults"/> fills in sensible per-kind
    /// numbers on ready so the rig always gets valid values.
    /// </summary>
    [Export] public ComponentStats Stats { get; set; } = new();

    /// <summary>
    /// The vehicle rig this component belongs to (set by the rig on ready). Lets a
    /// component query its host (e.g. "am I on a plane that is airborne?").
    /// </summary>
    public VehicleRig? Host { get; private set; }

    /// <summary>
    /// Fill in sensible per-kind defaults for any stat that is still at its zero
    /// initial value. Call this from a subclass _Ready AFTER setting Stats.Kind, so
    /// a freshly-dropped node (empty Stats) still contributes valid numbers to the
    /// rig. It never overwrites a value the user has already set (non-zero).
    /// </summary>
    protected internal virtual void EnsureDefaults()
    {
        var s = Stats;
        switch (s.Kind)
        {
            case ComponentKind.Airfoil:
                if (s.Area <= 0f) s.Area = 10f;
                if (s.AspectRatio <= 0f) s.AspectRatio = 5f;
                if (s.CdZero <= 0f) s.CdZero = 0.02f;
                if (s.Mass <= 0f) s.Mass = 500f;
                break;
            case ComponentKind.Engine:
                if (s.MaxThrust <= 0f) s.MaxThrust = 40000f;
                if (s.FuelFlowPerSec <= 0f) s.FuelFlowPerSec = 1.0f;
                if (s.FuelCapacity <= 0f) s.FuelCapacity = 1500f;
                if (s.Mass <= 0f) s.Mass = 800f;
                break;
            case ComponentKind.Rotor:
                if (s.MaxThrust <= 0f) s.MaxThrust = 30000f;
                if (s.Blades <= 0) s.Blades = 4;
                if (s.Mass <= 0f) s.Mass = 600f;
                break;
            case ComponentKind.LandingGear:
                if (s.Wheels <= 0) s.Wheels = 1;
                if (s.Mass <= 0f) s.Mass = 100f;
                break;
            case ComponentKind.Pilot:
                if (s.Crew <= 0f) s.Crew = 1f;
                break;
            case ComponentKind.Radar:
                if (s.DetectRange <= 0f) s.DetectRange = 80000f;
                if (s.ScanRate <= 0f) s.ScanRate = 0.5f;
                break;
            case ComponentKind.Gun:
                if (s.FireRate <= 0f) s.FireRate = 30f;
                if (s.Damage <= 0f) s.Damage = 10f;
                if (s.Range <= 0f) s.Range = 1000f;
                if (s.BulletSpeed <= 0f) s.BulletSpeed = 900f;
                if (s.Mass <= 0f) s.Mass = 100f;
                break;
            case ComponentKind.Missile:
                if (s.MissileSpeed <= 0f) s.MissileSpeed = 500f;
                if (s.MissileRange <= 0f) s.MissileRange = 60000f;
                if (s.MissileCount <= 0) s.MissileCount = 1;
                if (s.Mass <= 0f) s.Mass = 250f;
                break;
            case ComponentKind.Armor:
                if (s.ArmorHealth <= 0f) s.ArmorHealth = 100f;
                break;
            case ComponentKind.Hardpoint:
            case ComponentKind.Anchor:
                break; // no required defaults
        }
    }

    /// <summary>Call by the rig on ready to register this component with its host.</summary>
    internal void AttachToHost(VehicleRig rig) => Host = rig;

    // --- Behavior hooks (client-side; the server never calls these) ----------
    /// <summary>
    /// Client-side per-frame behavior. Subclasses override to animate (spin a
    /// prop, run a radar sweep, flash a muzzle). Not called on the headless server.
    /// </summary>
    protected internal virtual void OnClientProcess(double delta) { }

    /// <summary>
    /// Client-side setup: build any child visual nodes (muzzle flash, prop disc,
    /// gear wheels) the component needs for rendering. Subclasses override.
    /// </summary>
    protected internal virtual void OnClientReady() { }

    /// <summary>
    /// Client-side teardown. Subclasses override to clean up any resources they
    /// created in <see cref="OnClientReady"/>.
    /// </summary>
    protected internal virtual void OnClientExitTree() { }
}
