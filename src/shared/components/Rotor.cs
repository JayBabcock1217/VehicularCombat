using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// A rotor system: main rotor or tail rotor for helicopters. Drop one per rotor,
/// position it (main rotor at the top, tail rotor at the tail), and set
/// <see cref="VehicleComponent.Stats"/> (MaxThrust for lift, Blades for the
/// visual). The rig marks the vehicle as rotor-capable and sums rotor thrust.
///
/// On the client this node spins a visible rotor + a faint disc. No flight
/// behavior yet.
/// </summary>
[GlobalClass]
public partial class Rotor : VehicleComponent
{
    private Node3D _disc;
    private float _speed; // 0..1

    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Rotor;
        EnsureDefaults();
        base._Ready();
    }

    /// <summary>Set rotor speed 0..1 (the flight model calls this).</summary>
    public void SetSpeed(float s) => _speed = Mathf.Clamp(s, 0f, 1f);

    protected internal override void OnClientReady()
    {
        int blades = Mathf.Max(Stats.Blades, 2);
        _disc = new Node3D { Name = "RotorDisc" };
        AddChild(_disc);
        for (int i = 0; i < blades; i++)
        {
            float ang = (float)(i * Mathf.Tau / blades);
            var blade = new MeshInstance3D();
            blade.Mesh = new BoxMesh { Size = new Vector3(9f, 0.1f, 0.4f) };
            blade.MaterialOverride = CompMat.Std(new Color(0.15f, 0.15f, 0.15f), 0.8f);
            blade.Position = new Vector3(4.5f, 0f, 0f);
            var pivot = new Node3D();
            pivot.Rotation = new Vector3(0f, ang, 0f);
            pivot.AddChild(blade);
            _disc.AddChild(pivot);
        }
        // A faint disc so the rotor disc is visible at speed.
        var disc = new MeshInstance3D
        {
            Name = "Disc",
            Mesh = new CylinderMesh { TopRadius = 4.6f, BottomRadius = 4.6f, Height = 0.05f },
        };
        disc.MaterialOverride = CompMat.Translucent(new Color(1f, 1f, 1f, 0.08f));
        _disc.AddChild(disc);
    }

    protected internal override void OnClientProcess(double delta)
    {
        if (_disc != null)
            _disc.RotateX(_speed * 30f * (float)delta);
    }
}
