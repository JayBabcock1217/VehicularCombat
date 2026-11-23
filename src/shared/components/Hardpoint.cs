using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// An empty hardpoint / weapon attachment point — a mount on the wing or belly
/// where external stores (missiles, bombs, fuel tanks) can be attached later.
/// Drop one per hardpoint, position it, and (optionally) give it a
/// <see cref="VehicleComponent.Stats.Label"/> (e.g. "Wing-Left-1"). The rig lists
/// all hardpoints so the UI / combat system can show available stores.
///
/// On the client this node draws a small bracket so the empty mount is visible.
/// </summary>
[GlobalClass]
public partial class Hardpoint : VehicleComponent
{
    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Hardpoint;
        EnsureDefaults();
        base._Ready();
    }

    protected internal override void OnClientReady()
    {
        var bracket = new MeshInstance3D
        {
            Name = "Bracket",
            Mesh = new BoxMesh { Size = new Vector3(1.2f, 0.3f, 0.3f) },
        };
        bracket.MaterialOverride = CompMat.Std(new Color(0.55f, 0.55f, 0.58f), 0.6f, 0.5f);
        AddChild(bracket);

        var pin = new MeshInstance3D
        {
            Name = "Pin",
            Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.5f },
            Position = new Vector3(0f, 0.3f, 0f),
        };
        pin.MaterialOverride = CompMat.Std(new Color(0.3f, 0.3f, 0.32f), 0.5f, 0.7f);
        AddChild(pin);
    }
}
