using Godot;

namespace VehicularCombat.Shared;

/// <summary>
/// A vehicle stand-in entity. The SAME class runs on the server (authoritative
/// motion state) and the client (render + local prediction).
///
/// Stage 0 motion is minimal and class-appropriate, driven by
/// <see cref="VehicleClassDef"/>: planar classes (ground/naval) move on a flat
/// plane with W=forward, A/D=turn; aerial classes (air/heli) add pitch to
/// climb/descend. Real flight/ground models come in later stages.
///
/// This is deliberately the ONLY vehicle-specific motion code. Adding a new
/// class = a JSON entry + (optionally) an override of the motion step. The
/// spawner and protocol do not change (GOALS.md §7.1 "no rework").
/// </summary>
public partial class VehicleStandin : Node3D
{
    /// <summary>Unique id assigned by the server. -1 until spawned.</summary>
    public int Id { get; set; } = -1;

    public VehicleClassId Class { get; set; } = VehicleClassId.Unknown;
    public int Faction { get; set; } = -1;
    public bool Alive { get; set; } = true;

    /// <summary>
    /// The motion state. Both the server (authoritative) and the client (local
    /// prediction) keep a MotionState and advance it with the SAME pure step
    /// function, so the client's prediction matches the server given identical
    /// inputs. See <see cref="Step"/>.
    /// </summary>
    public MotionState Motion { get; } = new();

    private VehicleClassDef? _def;

    public override void _Ready()
    {
        _def = VehicleClassRegistry.Get(Class);
    }

    /// <summary>Configure this stand-in on spawn (server or client).</summary>
    public void Configure(int id, VehicleClassId cls, int faction, Vector3 spawnPos)
    {
        Id = id;
        Class = cls;
        Faction = faction;
        Alive = true;
        _def = VehicleClassRegistry.Get(cls);
        Motion.Reset(spawnPos);
    }

    /// <summary>
    /// Integrate one step using the current <see cref="Motion"/> and this
    /// stand-in's class def. <paramref name="dt"/> in seconds.
    /// </summary>
    public void Step(float dt, float throttle, float turn, float pitch)
    {
        if (!Alive || _def == null) return;
        Motion.Step(dt, throttle, turn, pitch, _def);
        // Mirror the motion state into the node for rendering.
        Position = Motion.Position;
        Rotation = Motion.Rotation;
    }
}

/// <summary>
/// Pure, deterministic motion state + step. Identical on server and client, so
/// the client can predict its own vehicle between server snapshots and reconcile
/// on receipt (GOALS.md §4.1 "client prediction + server reconciliation").
/// </summary>
public partial class MotionState
{
    public Vector3 Position { get; private set; }
    public Vector3 Rotation { get; private set; } // Euler angles (yaw in Y)
    public Vector3 Velocity { get; private set; }

    public void Reset(Vector3 pos)
    {
        Position = pos;
        Rotation = Vector3.Zero;
        Velocity = Vector3.Zero;
    }

    /// <summary>
    /// Set the motion state directly (used by the client to seed local prediction
    /// from a server transform, and to reconcile). Bypasses <see cref="Step"/>.
    /// </summary>
    public void SetState(Vector3 pos, Vector3 rot, Vector3 vel)
    {
        Position = pos;
        Rotation = rot;
        Velocity = vel;
    }

    /// <summary>
    /// Advance the motion state by <paramref name="dt"/> seconds for the given
    /// class def and inputs (throttle/turn/pitch in -1..1).
    /// </summary>
    public void Step(float dt, float throttle, float turn, float pitch, VehicleClassDef def)
    {
        if (def == null) return;

        float speed = Mathf.Clamp(throttle, -1f, 1f) * def.MaxSpeed;

        // Turn heading (yaw) by turn * turn_rate.
        float yaw = Rotation.Y + turn * def.TurnRate * dt;
        var newRot = new Vector3(Rotation.X, yaw, Rotation.Z);
        var basis = Basis.FromEuler(newRot);

        // Forward from current heading.
        Vector3 fwd = basis * Vector3.Forward;

        // Aerial classes add vertical from pitch.
        float vertical = 0f;
        if (def.Domain == VehicleClassDef.MotionDomain.Aerial)
        {
            vertical = Mathf.Clamp(pitch, -1f, 1f) * def.MaxSpeed * 0.6f;
        }

        Vector3 targetVel = fwd * speed + Vector3.Up * vertical;
        float damping = 1f - Mathf.Exp(-4f * dt);
        Velocity = Velocity.Lerp(targetVel, damping);
        Position += Velocity * dt;

        // Planar classes stay grounded (y = 0).
        if (def.Domain == VehicleClassDef.MotionDomain.Planar)
        {
            Position = new Vector3(Position.X, 0f, Position.Z);
        }

        Rotation = newRot;
    }
}
