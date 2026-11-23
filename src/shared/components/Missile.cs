using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// A missile / guided-weapon launcher. Drop one per launcher (wing, belly, or
/// APU), position it, and set <see cref="VehicleComponent.Stats"/> (MissileSpeed,
/// MissileRange, MissileDamage, MissileCount). The rig adds it to the weapon list
/// flagged as a missile.
///
/// On the client this node draws a rail + a missile on it. No launch logic yet.
/// </summary>
[GlobalClass]
public partial class Missile : VehicleComponent
{
    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Missile;
        EnsureDefaults();
        base._Ready();
    }

    protected internal override void OnClientReady()
    {
        var rail = new MeshInstance3D
        {
            Name = "Rail",
            Mesh = new BoxMesh { Size = new Vector3(2f, 0.1f, 0.4f) },
            Position = new Vector3(1f, 0f, 0f),
        };
        rail.MaterialOverride = CompMat.Std(new Color(0.4f, 0.4f, 0.42f), 0.5f, 0.5f);
        AddChild(rail);

        var body = new MeshInstance3D
        {
            Name = "MissileBody",
            Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.15f, Height = 1.6f },
            Position = new Vector3(1.2f, 0.25f, 0f),
            Rotation = new Vector3(0f, 0f, -Mathf.Pi / 2f),
        };
        body.MaterialOverride = CompMat.Std(new Color(0.7f, 0.7f, 0.72f), 0.5f, 0.3f);
        AddChild(body);

        var nose = new MeshInstance3D
        {
            Name = "MissileNose",
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.15f, Height = 0.4f },
            Position = new Vector3(2.15f, 0.25f, 0f),
            Rotation = new Vector3(0f, 0f, -Mathf.Pi / 2f),
        };
        nose.MaterialOverride = CompMat.Std(new Color(0.7f, 0.7f, 0.72f), 0.5f, 0.3f);
        AddChild(nose);
    }
}
