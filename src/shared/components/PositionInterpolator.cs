using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// Manually interpolates a <c>RigidBody3D</c>'s position between the last two
/// physics ticks so the render loop (which runs faster than physics) sees a
/// smooth position. This is the standard "manual interpolation" pattern that
/// most games use when Godot's built-in <c>GetGlobalTransformInterpolated()</c>
/// doesn't work correctly (e.g. force-driven Jolt bodies).
///
/// How it works:
///   - Each physics tick, we record the body's CURRENT position as <c>_prevPos</c>
///     (shifting the old current to previous) and store the tick timestamp.
///   - In the render loop (<c>_Process</c>), we lerp between <c>_prevPos</c> and
///     the body's current position based on how far we are between the last two
///     physics ticks.
///
/// The camera (or anything else that needs a smooth position) reads
/// <see cref="InterpolatedPosition"/> instead of the body's raw
/// <c>GlobalPosition</c>.
///
/// This node must be a child of the <c>RigidBody3D</c> it interpolates.
/// </summary>
[GlobalClass]
public partial class PositionInterpolator : Node
{
    private RigidBody3D? _body;
    private Vector3 _prevPos;
    private Vector3 _currPos;
    private double _prevTickTime;
    private bool _initialized;

    /// <summary>
    /// The smooth, interpolated position of the body. Reads this instead of
    /// <c>body.GlobalPosition</c> for camera/render purposes.
    /// </summary>
    public Vector3 InterpolatedPosition { get; private set; }

    public override void _Ready()
    {
        _body = GetParent() as RigidBody3D;
        if (_body == null)
        {
            GD.PushWarning($"PositionInterpolator '{Name}': expected a RigidBody3D parent.");
        }
        _prevPos = _body?.Position ?? Vector3.Zero;
        _currPos = _prevPos;
        InterpolatedPosition = _prevPos;
        _prevTickTime = Time.GetTicksMsec() / 1000.0;
        _initialized = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_body == null || !_initialized) return;
        // Shift: the current position becomes the previous, and we record the
        // new current. This gives us the bracket [prev, curr] that the render
        // loop will lerp within.
        _prevPos = _currPos;
        _currPos = _body.Position;
        _prevTickTime = Time.GetTicksMsec() / 1000.0;
    }

    public override void _Process(double delta)
    {
        if (_body == null || !_initialized) return;
        double now = Time.GetTicksMsec() / 1000.0;
        float tickInterval = 1f / Engine.GetPhysicsTicksPerSecond();
        // How far are we into the current physics step? 0..1 (clamped).
        float alpha = Mathf.Clamp((float)((now - _prevTickTime) / tickInterval), 0f, 1f);
        // Lerp between the last two physics positions.
        InterpolatedPosition = _prevPos.Lerp(_currPos, alpha);
    }
}
