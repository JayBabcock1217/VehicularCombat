using Godot;
using Godot.Collections;

namespace VehicularCombat.Shared;

/// <summary>
/// The network protocol: typed message shapes + serialization helpers shared by
/// the client and server. The server is authoritative — clients send *inputs*
/// and *requests*; the server sends *snapshots* and *results*.
///
/// Transport: Godot MultiplayerAPI over ENet. Messages are serialized to
/// Godot.Collections.Dictionary (JSON-friendly, works with rpc + var args).
/// </summary>
public static class NetProtocol
{
    // --- Message names (RPC method names + payload discriminators) ---------
    // Client -> Server
    public const string Join = "join";
    public const string SpawnRequest = "spawn_request";
    public const string Input = "input";
    public const string RespecFaction = "respec_faction";

    // Server -> Client
    public const string WorldSnapshot = "world_snapshot";
    public const string VehicleSnapshot = "vehicle_snapshot";
    public const string BaseEvent = "base_event";
    public const string SpawnResult = "spawn_result";
    public const string AuditNotice = "audit_notice";

    // --- Spawn result codes -------------------------------------------------
    public const int SpawnOk = 0;
    public const int SpawnErrInsufficientResources = 1;
    public const int SpawnErrNoFreeSlot = 2;
    public const int SpawnErrBadFaction = 3; // requested faction does not exist
    public const int SpawnErrBadBase = 4;    // base not found, or not spawnable by this faction
    public const int SpawnErrBadClass = 5;   // vehicle class id unknown/invalid

    // ==================================================================
    // Client -> Server
    // ==================================================================

    /// <summary>
    /// A client requests to join the world. The server assigns a peer id and
    /// echoes back a WorldSnapshot. <paramref name="faction"/> is the faction the
    /// player will act for (GOALS.md §5.2 two factions).
    /// </summary>
    public static Dictionary JoinMsg(int faction, VehicleClassId preferredClass) => new()
    {
        ["faction"] = faction,
        ["preferred_class"] = (int)preferredClass,
    };

    /// <summary>
    /// A client requests to spawn a vehicle stand-in at a base. The server checks
    /// the faction resource pool (cost) and a free spawn slot, then responds with
    /// a <see cref="SpawnResult"/>. <paramref name="slot"/>: 0 = deep (own territory),
    /// 1 = frontline (GOALS.md §5.4).
    /// </summary>
    public static Dictionary SpawnRequestMsg(string baseId, VehicleClassId cls, int slot) => new()
    {
        ["base_id"] = baseId,
        ["class"] = (int)cls,
        ["slot"] = slot,
    };

    /// <summary>
    /// A client's continuous input stream (~20 Hz). The server integrates this to
    /// move the player's vehicle; the client also predicts locally.
    /// </summary>
    public static Dictionary InputMsg(
        float throttle,   // -1..1 forward/back
        float turn,       // -1..1 turn
        float pitch,      // -1..1 pitch (aerial classes)
        float aimYaw,     // aim yaw (radians)
        float aimPitch,   // aim pitch (radians)
        bool firing) => new()
    {
        ["throttle"] = throttle,
        ["turn"] = turn,
        ["pitch"] = pitch,
        ["aim_yaw"] = aimYaw,
        ["aim_pitch"] = aimPitch,
        ["firing"] = firing,
    };

    /// <summary>A client requests to switch factions (server-validated).</summary>
    public static Dictionary RespecFactionMsg(int faction) => new()
    {
        ["faction"] = faction,
    };

    // ==================================================================
    // Server -> Client
    // ==================================================================

    /// <summary>
    /// Authoritative world state: factions (pool + name + color) and bases
    /// (owner + capture progress). Sent ~5 Hz.
    /// </summary>
    public static Dictionary WorldSnapshotMsg(
        Godot.Collections.Array factions,   // each: {id, name, color:[r,g,b,a], pool}
        Godot.Collections.Array bases,      // each: {id, owner, capture_progress, capturing}
        Godot.Collections.Array vehicles)   // see VehicleSnapshotMsg per entry
    {
        return new()
        {
            ["factions"] = factions,
            ["bases"] = bases,
            ["vehicles"] = vehicles,
        };
    }

    /// <summary>
    /// Per-vehicle state. Sent per-vehicle ~20 Hz. <paramref name="vehicles"/> is a
    /// Godot.Collections.Array of per-vehicle dictionaries.
    /// </summary>
    public static Dictionary VehicleSnapshotMsg(Godot.Collections.Array vehicles) => new()
    {
        ["vehicles"] = vehicles,
    };

    /// <summary>Build a single vehicle snapshot entry.</summary>
    public static Dictionary VehicleEntry(
        int vehicleId, VehicleClassId cls, int faction,
        Vector3 pos, Vector3 rot, Vector3 vel, bool alive) => new()
    {
        ["id"] = vehicleId,
        ["class"] = (int)cls,
        ["faction"] = faction,
        ["pos"] = new float[] { pos.X, pos.Y, pos.Z },
        ["rot"] = new float[] { rot.X, rot.Y, rot.Z },
        ["vel"] = new float[] { vel.X, vel.Y, vel.Z },
        ["alive"] = alive,
    };

    /// <summary>A base ownership / capture update.</summary>
    public static Dictionary BaseEventMsg(string baseId, int newOwner, float captureProgress, int capturingFaction) => new()
    {
        ["base_id"] = baseId,
        ["owner"] = newOwner,
        ["capture_progress"] = captureProgress,
        ["capturing"] = capturingFaction,
    };

    /// <summary>The result of a <see cref="SpawnRequest"/> for the requesting peer.</summary>
    public static Dictionary SpawnResultMsg(int code, int vehicleId = -1, string reason = "") => new()
    {
        ["code"] = code,
        ["vehicle_id"] = vehicleId,
        ["reason"] = reason,
    };

    /// <summary>An integrity / audit notice (anomaly detected, input rejected, etc.).</summary>
    public static Dictionary AuditNoticeMsg(string level, string detail) => new()
    {
        ["level"] = level,
        ["detail"] = detail,
    };
}

/// <summary>
/// Small helpers for building the WorldSnapshot's per-faction and per-base entries.
/// </summary>
public static class NetProtocolHelpers
{
    public static Dictionary FactionEntry(FactionDef f) => new()
    {
        ["id"] = f.Id,
        ["name"] = f.Name,
        ["color"] = new float[] { f.Color.R, f.Color.G, f.Color.B, f.Color.A },
        ["pool"] = f.ResourcePool,
    };

    public static Dictionary BaseEntry(BaseDef b) => new()
    {
        ["id"] = b.Id,
        ["owner"] = b.OwnerFaction,
        ["capture_progress"] = b.CaptureProgress,
        ["capturing"] = b.CapturingFaction,
    };
}
