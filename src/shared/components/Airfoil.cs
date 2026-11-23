using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// An aerodynamic surface: wing, tailplane, or a fuselage/structure section.
/// Drop one per surface, position it on the mesh (e.g. the wing root), and set
/// <see cref="VehicleComponent.Stats"/> (Area, AspectRatio, ClMax, CdZero) to
/// define its lift/drag. The flight model sums all airfoils for the plane's
/// total wing area, drag, and max lift.
///
/// On the client this node draws a faint translucent plate at its local transform
/// so the airfoil you placed is visible in the editor / in-game. No behavior yet
/// — the aerodynamic math comes with the flight model.
/// </summary>
[GlobalClass]
public partial class Airfoil : VehicleComponent
{
    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Airfoil;
        EnsureDefaults();
        base._Ready();
    }

    /// <summary>
    /// Client-only: draw a thin translucent plate so the airfoil is visible. Size
    /// is derived from Area (sqrt gives a representative span).
    /// </summary>
    protected internal override void OnClientReady()
    {
        float span = Mathf.Sqrt(Mathf.Max(Stats.Area, 1f)) * 2f;
        var plate = new MeshInstance3D
        {
            Name = "AirfoilSurface",
            Mesh = new BoxMesh { Size = new Vector3(span, 0.15f, 2f) },
        };
        plate.MaterialOverride = CompMat.Translucent(new Color(0.4f, 0.7f, 1f, 0.25f));
        AddChild(plate);
    }
}
