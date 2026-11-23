using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// The cockpit / pilot. Drop ONE per aircraft, position it at the pilot's seat
/// (the camera anchor). Set <see cref="VehicleComponent.Stats"/> (Crew,
/// ControlAuthority). The rig marks the vehicle as piloted and records crew +
/// control responsiveness.
///
/// This node is also the natural CAMERA ANCHOR: the third-person camera (or a
/// future cockpit camera) can be parented to <see cref="CameraAnchor"/> or target
/// its global position. On the client it draws a small cockpit marker.
/// </summary>
[GlobalClass]
public partial class Pilot : VehicleComponent
{
    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Pilot;
        EnsureDefaults();
        base._Ready();
    }

    /// <summary>
    /// The camera anchor node (a bare Node3D at the pilot's head/eye position).
    /// Cameras can be parented to it, or target <c>GlobalPosition</c>.
    /// </summary>
    public Node3D CameraAnchor { get; private set; }

    protected internal override void OnClientReady()
    {
        CameraAnchor = new Node3D { Name = "CameraAnchor" };
        AddChild(CameraAnchor);

        var marker = new MeshInstance3D
        {
            Name = "CockpitMarker",
            Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f },
            Position = new Vector3(0f, 0.3f, 0f),
        };
        marker.MaterialOverride = CompMat.Translucent(new Color(1f, 0.85f, 0.2f, 0.8f));
        AddChild(marker);
    }
}
