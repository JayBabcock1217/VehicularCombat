using Godot;
using Godot.Collections;
using VehicularCombat.Shared;

namespace VehicularCombat.Server;

/// <summary>
/// Manages player sessions: join/leave, faction membership, and the mapping from
/// peer id -> the player's vehicle. Validates all client requests
/// (server-authoritative, GOALS.md §4.1).
///
/// The actual RPC *methods* live on the <see cref="Server"/> node (Godot C# RPCs
/// are peer-agnostic and must be defined on the node that owns the
/// MultiplayerPeer). This class holds the *logic* and is called by those RPCs.
/// </summary>
public partial class PlayerManager : Node
{
    public class Player
    {
        public int PeerId;
        public int Faction;
        public VehicleClassId PreferredClass;
        public int VehicleId = -1;   // -1 = not spawned
        public bool Spawned => VehicleId >= 0;
        // Server's view of the player's last input (used each tick to move the vehicle).
        public float Throttle, Turn, Pitch, AimYaw, AimPitch;
        public bool Firing;
        // For teleport detection.
        public Vector3 LastPos;
        public bool HasLastPos;
    }

    private WorldState _world;
    private Economy _economy;
    private VehicleSpawner _spawner;
    private VehicleRegistry _vehicles;
    private Integrity _integrity;

    private readonly System.Collections.Generic.Dictionary<int, Player> _players = new();

    public void Setup(WorldState world, Economy economy, VehicleSpawner spawner,
        VehicleRegistry vehicles, Integrity integrity)
    {
        _world = world; _economy = economy; _spawner = spawner;
        _vehicles = vehicles; _integrity = integrity;
    }

    public Player? Get(int peerId) => _players.TryGetValue(peerId, out var p) ? p : null;
    public System.Collections.Generic.IEnumerable<Player> All() => _players.Values;

    // --- Logic handlers (called by the Server's RPC methods) ---------------

    public void HandleJoin(int peerId, Dictionary msg)
    {
        int faction = (int)msg["faction"];
        if (faction < 0 || faction >= _world.Factions.Length) faction = Factions.Alpha;
        _players[peerId] = new Player
        {
            PeerId = peerId,
            Faction = faction,
            PreferredClass = (VehicleClassId)(int)msg["preferred_class"],
        };
        _integrity.Log("info", "join", $"peer {peerId} joined as faction {faction}");
        GD.Print($"PlayerManager: peer {peerId} joined (faction {faction})");
    }

    public void HandleSpawnRequest(int peerId, Dictionary msg)
    {
        var p = _players[peerId];
        string baseId = (string)msg["base_id"];
        var cls = (VehicleClassId)(int)msg["class"];
        int slot = (int)msg["slot"];

        var (code, vehicleId) = _economy.TrySpawn(_world, _spawner, p.Faction, cls, baseId, slot);

        if (code == NetProtocol.SpawnOk)
        {
            p.VehicleId = vehicleId;
            var v = _vehicles.Get(vehicleId);
            if (v != null) { p.LastPos = v.Motion.Position; p.HasLastPos = true; }
            _integrity.Log("info", "spawn", $"peer {peerId} spawned vehicle {vehicleId} ({cls}) at {baseId} slot={slot}");
        }
        else
        {
            _integrity.Log("warn", "spawn_rejected", $"peer {peerId} spawn {cls} at {baseId} -> code {code}");
        }

        // Reply to this peer only, sent from the server ROOT node (path
        // "Server/Client") so the target path matches the client's root node,
        // which declares the SpawnResult [Rpc] handler. See Server.SpawnResult.
        ((Server)GetParent()).SendSpawnResult(peerId, code, vehicleId);
    }

    public void HandleInput(int peerId, Dictionary msg)
    {
        var p = _players[peerId];
        if (!p.Spawned) return;

        var clsDef = VehicleClassRegistry.Get(p.PreferredClass);
        if (clsDef != null && !_integrity.ValidateInput(msg, clsDef))
            return; // discard implausible input (logged by ValidateInput)

        p.Throttle = (float)msg["throttle"];
        p.Turn = (float)msg["turn"];
        p.Pitch = (float)msg["pitch"];
        p.AimYaw = (float)msg["aim_yaw"];
        p.AimPitch = (float)msg["aim_pitch"];
        p.Firing = (bool)msg["firing"];
    }

    public void HandleRespecFaction(int peerId, Dictionary msg)
    {
        var p = _players[peerId];
        int faction = (int)msg["faction"];
        if (faction < 0 || faction >= _world.Factions.Length) return;
        p.Faction = faction;
        _integrity.Log("info", "respec", $"peer {peerId} -> faction {faction}");
    }

    public void HandlePlayerExit(int peerId)
    {
        if (_players.TryGetValue(peerId, out var p))
        {
            if (p.Spawned) _vehicles.Remove(p.VehicleId);
            _players.Remove(peerId);
            _integrity.Log("info", "leave", $"peer {peerId} left");
        }
    }

    /// <summary>Integrate all spawned vehicles one tick from each player's last input.</summary>
    public void TickVehicles(float dt)
    {
        if (_vehicles == null || _integrity == null) return;
        foreach (var p in _players.Values)
        {
            if (!p.Spawned) continue;
            var v = _vehicles.Get(p.VehicleId);
            if (v == null) { p.VehicleId = -1; continue; }

            v.Step(dt, p.Throttle, p.Turn, p.Pitch);

            if (p.HasLastPos)
                _integrity.CheckTeleport(p.VehicleId, p.LastPos, v.Motion.Position, 100f);
            p.LastPos = v.Motion.Position;
            p.HasLastPos = true;
        }
    }
}
