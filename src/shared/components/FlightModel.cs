using Godot;
using System.Collections.Generic;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// The FLIGHT MODEL: a CUSTOM KINEMATIC integrator for an aircraft.
///
/// WAR-THUNDER MODEL: the client runs its OWN flight simulation every RENDER
/// frame. It computes thrust, drag, and gravity, integrates them into
/// position/velocity, and sets the vehicle ROOT's transform directly. The
/// vehicle root is a plain <c>Node3D</c> (NOT a <c>RigidBody3D</c>) — physics
/// is completely decoupled from the visual. Ground contact is pure math
/// (step 6), so no physics body is needed at all. (An older revision kept a
/// frozen <c>RigidBody3D</c> "CollisionBody" child for collision; with physics
/// interpolation enabled, re-teleporting that body every render frame made the
/// renderer snap it forward once per physics tick — visible as jitter while
/// falling. It is no longer synced; see _Ready.)
///
/// This is why the camera never jitters: the root's position is a smooth
/// function of render time (integrated at 144Hz+), not stepped at 60Hz by a
/// physics engine. There is no physics interpolation, no force integration,
/// no ticking — just clean math at the render framerate.
///
/// Each render frame (<see cref="_Process"/>):
///   1. Ease throttle toward target at the spool rate.
///   2. Sum all engines' thrust (MaxThrust * throttle along local +X).
///   3. Compute drag (opposing velocity, scaled by CdZero + baseline).
///   4. Add gravity.
///   5. Integrate: velocity += (thrust+drag+gravity)/mass * dt
///      position += velocity * dt.
///   6. Ground contact: if below Y=0.5 and moving down, rest on the ground.
///   7. Set the root's position directly (visual + source of truth).
///
/// Runs on the CLIENT only (the server never instantiates this node).
/// </summary>
public partial class FlightModel : Node3D
{
    public float ThrottleTarget { get; set; }
    public float Throttle { get; private set; }

    [Export] public float BaselineDrag { get; set; } = 500f;
    [Export] public float MinMass { get; set; } = 1000f;
    [Export] public float Gravity { get; set; } = -9.8f;

    private Node3D? _root;       // the AirplaneRig (vehicle root, a Node3D)
    private AirplaneRig? _rig;
    private List<JetEngine> _engines = new();

    // Custom integrator state (source of truth).
    private Vector3 _position;
    private Vector3 _velocity;
    private float _mass = 1000f;
    private bool _ready;

    public Vector3 Velocity => _velocity;
    public float CurrentThrust { get; private set; }

    public override void _Ready()
    {
        _rig = GetParent() as AirplaneRig;
        _root = _rig; // the root IS the AirplaneRig (a Node3D)

        // NOTE: there used to be a frozen RigidBody3D "CollisionBody" child that
        // we re-teleported every frame. With physics interpolation enabled
        // (project setting), a body whose position is written every render frame
        // produces an inconsistent interpolation history — the renderer blends
        // between the LAST two physics frames while the root moves every render
        // frame, so the body (and the box collider that visually sits with the
        // model) lags the root by one physics step and snaps forward each tick.
        // That per-tick snap is the "jitter while falling". Ground contact is
        // handled in pure math below (see step 6), so the physics proxy is not
        // needed. If a CollisionBody is still in the scene, park it OFF the root
        // (deep under the ground, where it never intersects anything and renders
        // nothing visible) instead of syncing it.
        if (_rig != null && _rig.GetNodeOrNull<Node3D>("CollisionBody") is RigidBody3D cb)
        {
            cb.Freeze = true;
            cb.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            cb.CanSleep = false;
            cb.LinearVelocity = Vector3.Zero;
        }

        if (_rig == null)
            GD.PushWarning($"FlightModel '{Name}': expected an AirplaneRig parent.");
        else
            Callable.From(OnRigReady).CallDeferred();
    }

    private void OnRigReady()
    {
        if (_rig != null)
            _engines = _rig.GetComponents<JetEngine>();
        _mass = _rig != null && _rig.Aggregate.Mass > 0f ? _rig.Aggregate.Mass : MinMass;
        if (_root != null)
            _position = _root.Position;
        _velocity = Vector3.Zero;
        _ready = true;
        GD.Print($"FlightModel '{Name}': driving {_engines.Count} engine(s), mass={_mass:F0} kg");
    }

    public override void _Process(double delta)
    {
        if (!_ready || _root == null || _root.IsInsideTree() == false) return;
        float dt = (float)delta;
        if (dt <= 0f) return;

        // 1. Throttle spool.
        float spoolSec = SpoolUpSeconds();
        if (spoolSec > 0.001f)
        {
            float rate = 1f / spoolSec;
            if (Throttle < ThrottleTarget)
                Throttle = Mathf.Min(ThrottleTarget, Throttle + rate * dt);
            else
                Throttle = Mathf.Max(ThrottleTarget, Throttle - rate * dt);
        }
        else
            Throttle = Mathf.Clamp(ThrottleTarget, 0f, 1f);

        foreach (var e in _engines) e.SetThrottle(Throttle);

        // 2. Thrust.
        Vector3 totalThrust = Vector3.Zero;
        foreach (var e in _engines)
            totalThrust += e.ComputeThrust(Throttle);

        // 3. Drag.
        Vector3 drag = Vector3.Zero;
        if (_velocity.Length() > 0.01f)
        {
            float cd = BaselineDrag;
            if (_rig != null) cd += _rig.Aggregate.CdZero * 1000f;
            drag = -_velocity.Normalized() * (_velocity.Length() * cd);
        }

        // 4. Gravity.
        Vector3 grav = new Vector3(0f, Gravity, 0f) * _mass;

        // 5. Integrate.
        Vector3 accel = (totalThrust + drag + grav) / _mass;
        _velocity += accel * dt;
        _position += _velocity * dt;

        // 6. Ground contact.
        if (_position.Y < 0.5f && _velocity.Y < 0f)
        {
            _position = new Vector3(_position.X, 0.5f, _position.Z);
            _velocity = new Vector3(_velocity.X, 0f, _velocity.Z);
            _velocity.X *= 0.98f;
            _velocity.Z *= 0.98f;
        }

        // 7. Set the root's position (the visual + the source of truth).
        //    The root is a plain Node3D — no physics body in the tree — so the
        //    rendered transform is exactly the integrated value, every frame.
        //    This is crucial for avoiding jitter - we should NOT interpolate
        //    or defer position setting.
        if (_root != null)
        {
            // Explicitly set position directly without any additional processing
            _root.Position = _position;
        }

        CurrentThrust = totalThrust.Length();
    }

    private float SpoolUpSeconds()
    {
        float max = 0f;
        foreach (var e in _engines)
            max = Mathf.Max(max, e.Stats.SpoolUpSec);
        return max;
    }

    // --- Desync guard (War Thunder model) ------------------------------------
    [Export] public float CorrectThreshold { get; set; } = 20f;
    [Export] public float SnapThreshold { get; set; } = 100f;
    [Export] public float CorrectBlend { get; set; } = 0.7f;

    public void MaybeCorrectTo(Vector3 serverPos)
    {
        if (_root == null) return;
        float drift = (_root.Position - serverPos).Length();
        if (drift < CorrectThreshold) return;
        if (drift > SnapThreshold)
        {
            _position = serverPos;
            _velocity = Vector3.Zero;
            _root.Position = serverPos;
        }
        else
        {
            _position = _position.Lerp(serverPos, CorrectBlend);
            _root.Position = _position;
        }
    }
}
