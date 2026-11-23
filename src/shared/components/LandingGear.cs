using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// Landing gear: wheels + struts. Drop one per gear assembly (nose gear, main
/// gear), position it under the model, and set <see cref="VehicleComponent.Stats"/>
/// (Wheels, GearSpeedMax, Retractable, RetractSec). The rig marks the vehicle as
/// able to land and records the gear's max safe landing speed.
///
/// On the client this node draws the wheels + a strut. If
/// <see cref="Stats.Retractable"/>, the wheels fade when retracted (visual
/// stand-in). No landing physics yet.
/// </summary>
[GlobalClass]
public partial class LandingGear : VehicleComponent
{
    private readonly System.Collections.Generic.List<MeshInstance3D> _wheels = new();
    private bool _deployed = true;

    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.LandingGear;
        EnsureDefaults();
        base._Ready();
    }

    /// <summary>Deploy (true) or retract (false) the gear. The flight model calls this.</summary>
    public void SetDeployed(bool deployed) => _deployed = deployed;

    protected internal override void OnClientReady()
    {
        int n = Mathf.Max(Stats.Wheels, 1);
        float spread = 1.2f;
        for (int i = 0; i < n; i++)
        {
            float x = (i - (n - 1) / 2f) * spread;
            var wheel = new MeshInstance3D
            {
                Name = "Wheel_" + i,
                Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.3f },
                Position = new Vector3(x, 0f, 0f),
                Rotation = new Vector3(0f, 0f, Mathf.Pi / 2f),
            };
            wheel.MaterialOverride = CompMat.Std(new Color(0.1f, 0.1f, 0.1f, 1f), 0.6f);
            AddChild(wheel);
            _wheels.Add(wheel);
        }

        var strut = new MeshInstance3D
        {
            Name = "Strut",
            Mesh = new BoxMesh { Size = new Vector3(0.2f, 1.5f, 0.2f) },
            Position = new Vector3(0f, 0.75f, 0f),
        };
        strut.MaterialOverride = CompMat.Std(new Color(0.4f, 0.4f, 0.45f), 0.5f, 0.7f);
        AddChild(strut);
    }

    protected internal override void OnClientProcess(double delta)
    {
        if (Stats.Retractable)
        {
            float targetA = _deployed ? 1f : 0f;
            foreach (var w in _wheels)
            {
                var m = w.MaterialOverride as StandardMaterial3D;
                if (m == null) continue;
                var c = m.AlbedoColor;
                c.A = Mathf.Lerp(c.A, targetA, 0.1f);
                m.AlbedoColor = c;
            }
        }
    }
}
