using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// A named attachment point / anchor. Drop one anywhere on the model and give it
/// a <see cref="VehicleComponent.Stats.Label"/> (e.g. "Camera-Nose", "Smoke-Port").
/// The rig records all anchors by label, so other systems (cameras, VFX, future
/// attachments) can find a named point on the vehicle. This is the most generic
/// drop-in node — use it to mark ANY position on the mesh.
///
/// On the client this node draws a tiny gizmo so the anchor is visible in the
/// editor.
/// </summary>
[GlobalClass]
public partial class Anchor : VehicleComponent
{
    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Anchor;
        EnsureDefaults();
        base._Ready();
    }

    protected internal override void OnClientReady()
    {
        var gizmo = new MeshInstance3D
        {
            Name = "Gizmo",
            Mesh = new SphereMesh { Radius = 0.25f, Height = 0.5f },
        };
        gizmo.MaterialOverride = CompMat.Translucent(new Color(0.2f, 0.9f, 1f, 0.7f));
        AddChild(gizmo);
    }
}
