using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// A sensor suite: radar, gunsight, or FLIR. Drop one, position it (radar in the
/// nose / under the fuselage), and set <see cref="VehicleComponent.Stats"/>
/// (DetectRange, ScanRate, ConeFov). The rig records the vehicle's best sensor
/// (max detect range, fastest scan).
///
/// On the client this node draws a sweeping radar beam (a translucent cone that
/// oscillates in yaw at <see cref="Stats.ScanRate"/>). No detection logic yet.
/// </summary>
[GlobalClass]
public partial class Radar : VehicleComponent
{
    private Node3D _beamPivot;
    private float _phase;

    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Radar;
        EnsureDefaults();
        base._Ready();
    }

    protected internal override void OnClientReady()
    {
        float range = Mathf.Max(Stats.DetectRange, 10f);
        // A thin cone pointing +X (forward), pivoted in yaw to show the sweep.
        _beamPivot = new Node3D { Name = "BeamPivot" };
        AddChild(_beamPivot);

        var beam = new MeshInstance3D
        {
            Name = "Beam",
            Mesh = new CylinderMesh
            {
                TopRadius = 0f,
                BottomRadius = Mathf.Max(Stats.ConeFov, 5f) * 0.05f,
                Height = range * 0.2f,
            },
            Position = new Vector3(range * 0.1f, 0f, 0f),
            Rotation = new Vector3(0f, 0f, -Mathf.Pi / 2f), // cylinder axis along +X
        };
        beam.MaterialOverride = CompMat.Translucent(new Color(0.3f, 1f, 0.4f, 0.12f));
        _beamPivot.AddChild(beam);
    }

    protected internal override void OnClientProcess(double delta)
    {
        if (_beamPivot != null)
        {
            _phase += Stats.ScanRate * (float)delta * Mathf.Tau;
            _beamPivot.Rotation = new Vector3(0f, Mathf.Sin(_phase) * 0.6f, 0f);
        }
    }
}
