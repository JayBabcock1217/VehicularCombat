using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// Armor / structural plating. Drop one per armored section (fuel tank, cockpit,
/// engine), position it, and set <see cref="VehicleComponent.Stats"/>
/// (ArmorValue = flat damage reduction, ArmorHealth = HP it adds). The rig sums
/// armor for the vehicle's total damage resistance + structural HP.
///
/// On the client this node draws a faint armored plate. No damage logic yet.
/// </summary>
[GlobalClass]
public partial class Armor : VehicleComponent
{
    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Armor;
        EnsureDefaults();
        base._Ready();
    }

    protected internal override void OnClientReady()
    {
        var plate = new MeshInstance3D
        {
            Name = "ArmorPlate",
            Mesh = new BoxMesh { Size = new Vector3(2f, 1.5f, 1f) },
        };
        plate.MaterialOverride = CompMat.Translucent(new Color(0.5f, 0.55f, 0.6f, 0.15f), 0.4f, 0.9f);
        AddChild(plate);
    }
}
