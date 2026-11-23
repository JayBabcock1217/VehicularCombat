using Godot;

namespace VehicularCombat.Client;

/// <summary>
/// Third-person camera rig that follows the local vehicle and is driven by the
/// aim yaw/pitch (mouse-look). The camera ORBITS the vehicle's center: yaw/pitch
/// are the camera's orbit angles (full 360°), NOT the vehicle's own heading, so
/// moving the mouse looks all the way around while the vehicle keeps its heading.
///
/// SERVER-INDEPENDENT: this camera reads ONLY the local vehicle's rendered
/// position (interpolated) and the local mouse aim. It never touches server
/// data. The camera eases toward its desired orbit position each frame
/// (frame-rate-independent exponential), so it is always smooth — even when the
/// vehicle is accelerating, falling, or being corrected. The only snap is the
/// one-time snap when a NEW vehicle becomes the target (spawn); the camera
/// simply chases from then on, and a large gap is bridged by the same easing
/// (which converges quickly at high FollowSmooth).
///
/// When there is no local vehicle yet (before spawn), the camera idles in a high
/// overview of the spawn area.
/// </summary>
public partial class CameraRig : Node3D
{
    [Export] private float OrbitDistance = 26f;   // how far back the camera sits
    [Export] private float FollowSmooth = 10f;     // higher = snappier; ~10 is fluid, not rubber-bandy
    [Export] private float IdleHeight = 200f;      // overview height before spawning

    private Camera3D _cam;
    private Node3D _target;
    private InputController _input;
    private Vector3 _camPos;
    private Vector3 _lookPos;   // smoothed look-at point (what the camera aims at)
    private bool _posInitialized;

    public override void _Ready()
    {
        _cam = new Camera3D { Name = "Camera3D", Current = true, Fov = 72f, Position = new Vector3(0f, IdleHeight, -OrbitDistance) };
        AddChild(_cam);
        _target = new Node3D { Name = "TargetProxy" };
        AddChild(_target);
    }

    public void Setup(InputController input, Node3D localVehicle)
    {
        _input = input;
        if (localVehicle != null) _target = localVehicle;
    }

    /// <summary>
    /// Point the rig at the local vehicle (call after spawn).
    ///
    /// IMPORTANT: this is called on EVERY server snapshot (~25 Hz) for the local
    /// vehicle, so it MUST only reset the snap flag when the target node actually
    /// CHANGES. Unconditionally resetting _posInitialized made the next frame
    /// hard-snap _camPos/_lookPos to the vehicle's current orbit point ~25 times
    /// per second: with a stationary vehicle the target barely moves so the snap
    /// is invisible, but while falling under gravity the target moves every frame,
    /// so each snap teleported the camera forward by the accumulated easing lag —
    /// a 20–25 Hz sawtooth wobble of the whole view ("the vehicle stutters as it
    /// falls"). Snapping on every call was the jitter; snapping once when the
    /// tracked vehicle changes (spawn) is what _posInitialized is for.
    /// </summary>
    public void SetTarget(Node3D localVehicle)
    {
        if (localVehicle == null) return;
        bool targetChanged = !ReferenceEquals(_target, localVehicle);
        _target = localVehicle;
        if (targetChanged)
            _posInitialized = false; // snap to the new vehicle only once (on spawn)
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (dt <= 0f) return;

        float yaw = _input != null ? _input.SmoothAimYaw : 0f;
        float pitch = _input != null ? _input.SmoothAimPitch : -0.4f;

        Vector3 aimCenter;
        bool haveVehicle = _target != null && _target != GetNodeOrNull<Node3D>("TargetProxy");
        if (haveVehicle)
        {
            // The FlightModel integrates position at RENDER framerate and writes
            // the vehicle root's (LOCAL) Position every frame. The root's parent
            // (VehicleRoot) has a plain transform — no physics, no interpolation
            // — so the vehicle's true world position is just:
            //     parent.GlobalTransform * root.Position
            // Do NOT read _target.GlobalPosition here: with physics interpolation
            // enabled, the RigidBody3D parked inside the vehicle (CollisionBody)
            // makes the renderer offset the whole subtree once per physics tick,
            // and that offset is what GlobalPosition returns — a 1-tick-old
            // position that snaps forward every tick (the fall jitter). The root's
            // own Position is written at render rate and never lags, so
            // parent.GlobalTransform * root.Position is exact and smooth.
            var parent = _target.GetParent() as Node3D;
            if (parent != null)
            {
                aimCenter = parent.GlobalTransform * _target.Position;
            }
            else
            {
                // Fallback for cases where parent might be null, but should not happen
                aimCenter = _target.Position;
            }
        }
        else
        {
            aimCenter = new Vector3(-700f, 0f, -700f);
            pitch = -0.5f;
        }

        // Orbit direction from yaw/pitch (full circle in yaw).
        float cp = Mathf.Cos(pitch);
        Vector3 dir = new Vector3(cp * Mathf.Cos(yaw), Mathf.Sin(pitch), cp * Mathf.Sin(yaw));
        Vector3 desired = aimCenter + dir * OrbitDistance;
        if (desired.Y < 2.5f) desired.Y = 2.5f; // keep the camera above the ground

        // EASE both the camera position and the look-at point toward their
        // desired values. This is what makes the camera smooth:
        //   - _camPos eases toward `desired` (the orbit point)
        //   - _lookPos eases toward `aimCenter` (the vehicle's center)
        // Both use the same frame-rate-independent exponential, so the camera
        // never jumps — it always flows. On spawn the first frame snaps (so the
        // camera appears at the vehicle immediately); after that it eases.
        float t = 1f - Mathf.Exp(-FollowSmooth * dt);
        if (!_posInitialized)
        {
            _camPos = desired;
            _lookPos = aimCenter;
            _posInitialized = true;
        }
        else
        {
            _camPos = _camPos.Lerp(desired, t);
            _lookPos = _lookPos.Lerp(aimCenter, t);
        }

        _cam.GlobalPosition = _camPos;
        _cam.LookAtFromPosition(_cam.GlobalPosition, _lookPos, Vector3.Up);
    }
}
