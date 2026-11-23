using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// A powerplant: jet engine, piston engine, or turboprop. Drop one per engine,
/// position it (jet at the wing root / rear, piston in the nose), and set
/// <see cref="VehicleComponent.Stats"/> (MaxThrust, FuelFlowPerSec, FuelCapacity,
/// SpoolUpSec, Blades). The rig sums all engines for total thrust + fuel.
///
/// On the client this node animates: a prop spins if <see cref="Stats.Blades"/>
/// &gt; 0, and a jet exhaust glow pulses with throttle. No thrust is applied yet
/// — that happens with the flight model.
/// </summary>
[GlobalClass]
public partial class JetEngine : VehicleComponent
{
    private Node3D _prop;
    private MeshInstance3D _exhaust;
    private StandardMaterial3D _exhaustMat;
    private float _throttle; // 0..1, set externally (by the flight model later)

    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Engine;
        EnsureDefaults();
        base._Ready();
    }

    /// <summary>
    /// Set the current throttle 0..1. The flight model calls this each frame; the
    /// client uses it to drive the prop spin + exhaust glow.
    /// </summary>
    public void SetThrottle(float t) => _throttle = Mathf.Clamp(t, 0f, 1f);

    /// <summary>
    /// The thrust this engine produces at the given throttle (0..1), in WORLD
    /// space, along the engine's LOCAL +X (forward). The <see cref="FlightModel"/>
    /// sums these over all engines and applies each at the engine's transform.
    ///
    /// <c>F = MaxThrust * throttle</c>. The direction is the engine's local +X
    /// rotated by the node's world basis, so thrust always points the way the
    /// engine is facing — even if the engine is mounted at an angle.
    /// </summary>
    public Vector3 ComputeThrust(float throttle)
    {
        float t = Mathf.Clamp(throttle, 0f, 1f);
        float magnitude = Stats.MaxThrust * t;
        if (magnitude <= 0f) return Vector3.Zero;
        // Local +X (the engine's nose/forward) in world space.
        Vector3 forward = GlobalTransform.Basis * Vector3.Right;
        return forward * magnitude;
    }

    protected internal override void OnClientReady()
    {
        // Prop (if Blades > 0): a spinning set of blades around a hub.
        if (Stats.Blades > 0)
        {
            _prop = new Node3D { Name = "Prop" };
            AddChild(_prop);
            int blades = Mathf.Max(Stats.Blades, 2);
            for (int i = 0; i < blades; i++)
            {
                float ang = (float)(i * Mathf.Tau / blades);
                var blade = new MeshInstance3D();
                blade.Mesh = new BoxMesh { Size = new Vector3(0.15f, 0.15f, 3f) };
                blade.MaterialOverride = CompMat.Std(new Color(0.2f, 0.2f, 0.2f), 0.5f, 0.6f);
                blade.Position = new Vector3(0f, 0f, 1.5f);
                var pivot = new Node3D();
                pivot.Rotation = new Vector3(0f, ang, 0f);
                pivot.AddChild(blade);
                _prop.AddChild(pivot);
            }
        }

        // Jet exhaust glow (present for all; only bright when throttle > 0).
        _exhaustMat = CompMat.Emissive(new Color(1f, 0.5f, 0.1f), 0f, 0.3f);
        _exhaustMat.Transparency = Godot.BaseMaterial3D.TransparencyEnum.Alpha;
        _exhaustMat.AlbedoColor = new Color(1f, 0.5f, 0.1f, 0.6f);
        _exhaust = new MeshInstance3D
        {
            Name = "Exhaust",
            Mesh = new SphereMesh { Radius = 0.4f, Height = 1.2f },
            Position = new Vector3(-0.8f, 0f, 0f),
        };
        _exhaust.MaterialOverride = _exhaustMat;
        AddChild(_exhaust);
    }

    protected internal override void OnClientProcess(double delta)
    {
        if (_prop != null && Stats.Blades > 0)
        {
            float rpm = 0.3f + _throttle * 40f;
            _prop.RotateY(rpm * (float)delta);
        }
        CompMat.SetEmission(_exhaustMat, _throttle * 2f);
    }
}
