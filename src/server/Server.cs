using Godot;
using Godot.Collections;
using VehicularCombat.Shared;

namespace VehicularCombat.Server;

/// <summary>
/// The dedicated server root. Owns the MultiplayerPeer (ENet server), the fixed
/// tick loop, and is the RPC endpoint for clients. Run headless:
/// <c>godot --headless res://src/scenes/server.tscn -- --server [--port N]</c>
///
/// C# multiplayer (Godot 4.8 beta): [Rpc]-marked methods receive calls; the
/// server invokes them on peers via the dynamic
/// <c>Multiplayer.Rpc(peerId, this, "MethodName", argsArray)</c> overload. Peer
/// id 0 = broadcast to all; a specific id = one peer. Clients call the request
/// methods (Join/SpawnRequest/SendInput/RespecFaction); the server calls the snapshot
/// methods (WorldSnapshot/VehicleSnapshot) to push state.
///
/// Tick (20 Hz): integrate vehicles -> campaign -> economy -> AI defense.
/// Snapshots: WorldSnapshot ~5 Hz, VehicleSnapshot ~20 Hz.
/// </summary>
public partial class Server : Node
{
    private WorldState _world;
    private Persistence _persistence;
    private Campaign _campaign;
    private Economy _economy;
    private AiDefense _ai;
    private Integrity _integrity;
    private PlayerManager _players;
    private VehicleSpawner _spawner;
    private VehicleRegistry _vehicles;

    private ENetMultiplayerPeer _peer;
    private float _tickTimer, _worldSnapTimer, _vehicleSnapTimer, _saveTimer;
    private const float SaveInterval = 5f;

    public override void _Ready()
    {
        GD.Print("=== VehicularCombat SERVER (headless) ===");

        _world = new WorldState(); AddChild(_world);
        _persistence = new Persistence(); AddChild(_persistence);
        _campaign = new Campaign(); AddChild(_campaign);
        _economy = new Economy(); AddChild(_economy);
        _ai = new AiDefense(); AddChild(_ai);
        _integrity = new Integrity(); AddChild(_integrity);
        _players = new PlayerManager(); AddChild(_players);
        _spawner = new VehicleSpawner(); AddChild(_spawner);
        _vehicles = new VehicleRegistry(); AddChild(_vehicles);

        _persistence.Init(_world);
        if (!_persistence.TryLoad(_world))
        {
            _world.BuildDefaultWorld();
            _persistence.Save(_world.ToJson());
        }

        _campaign.Setup(_world, _vehicles);
        _economy.Setup(_world);
        _ai.Setup(_world, _vehicles);
        _spawner.Setup(_vehicles);
        _players.Setup(_world, _economy, _spawner, _vehicles, _integrity);

        int port = GameConfig.GetPort();
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateServer(port, 32);
        int attempts = 0;
        while (err != Error.Ok && attempts < 10)
        {
            // Port in use (a previous instance may not have released it yet) —
            // try the next port. The client should be told via --port to match.
            port++;
            attempts++;
            GD.PushWarning($"Server: port {port - 1} unavailable ({err}), retrying on {port}…");
            err = _peer.CreateServer(port, 32);
        }
        if (err != Error.Ok)
        {
            GD.PushError($"Server: ENet CreateServer failed after retries (last port {port}): {err}");
            return;
        }
        Multiplayer.MultiplayerPeer = _peer;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;

        GD.Print($"Server: listening on port {port}. {_world.Factions.Length} factions, {_world.Bases.Count} bases.");
        _integrity.Log("info", "server_start", $"port={port} bases={_world.Bases.Count}");
    }

    public override void _ExitTree()
    {
        _persistence.Save(_world.ToJson());
        _integrity.Log("info", "server_stop", "shutdown");
        GD.Print("Server: world saved, stopped.");
    }

    private void OnPeerConnected(long peerId) => _integrity.Log("info", "peer_connected", $"peer {peerId}");
    private void OnPeerDisconnected(long peerId) => _players.HandlePlayerExit((int)peerId);

    // ======================================================================
    // Client -> Server RPCs (clients call these; server receives)
    // ======================================================================

    // RPC arguments are Variant (NOT Godot.Collections.Dictionary): the Godot C#
    // source generator only marshals Variant/primitives/vectors, so a Dictionary
    // parameter makes the method "Godot-incompatible" and it is NOT registered as
    // an RPC (HasMethod returns false -> the RPC is silently dropped). See
    // docs/STAGE0.md (RPC signatures).
    //
    // RpcMode.AnyPeer: these are CLIENT->SERVER requests, so the server (authority)
    // must accept them from any client peer. The default (bare [Rpc]) is
    // RpcMode.Authority, which rejects calls from non-authority peers.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void Join(Variant msg)
        => _players.HandleJoin(Multiplayer.GetRemoteSenderId(), (Dictionary)msg);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void SpawnRequest(Variant msg)
        => _players.HandleSpawnRequest(Multiplayer.GetRemoteSenderId(), (Dictionary)msg);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void SendInput(Variant msg)
        => _players.HandleInput(Multiplayer.GetRemoteSenderId(), (Dictionary)msg);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void RespecFaction(Variant msg)
        => _players.HandleRespecFaction(Multiplayer.GetRemoteSenderId(), (Dictionary)msg);

    // ======================================================================
    // Server -> Client result RPCs. SpawnResult is sent to ONE peer (the one
    // that requested a spawn), but it is sent FROM THIS root node (path
    // "Server/Client") so its target path matches the client's root node —
    // the same node that carries every other [Rpc] method. PlayerManager
    // builds the reply and passes it here (see PlayerManager.HandleSpawnRequest).
    // ======================================================================

    // Sent to one specific peer, but declared AnyPeer so the client node's [Rpc]
    // set (and its RpcMode) matches this node exactly — required for the
    // per-node RPC checksum.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void SpawnResult(Variant msg) { }  // sent per-peer to the client root

    // ======================================================================
    // Server -> Client snapshot RPCs (server calls; client mirrors+receives).
    // The client node defines matching [Rpc] methods that actually apply state.
    // ======================================================================

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void WorldSnapshot(Variant msg) { }   // client mirrors this

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void VehicleSnapshot(Variant msg) { } // client mirrors this

    // --- helper: build an args array for a single-dict RPC -----------------
    private static Godot.Collections.Array OneArg(Dictionary d)
    {
        var a = new Godot.Collections.Array();
        a.Add(d);
        return a;
    }

    /// <summary>Send a spawn result to a single peer, from this root node.</summary>
    public void SendSpawnResult(int peerId, int code, int vehicleId)
        => Multiplayer.Rpc(peerId, this, "SpawnResult", OneArg(NetProtocol.SpawnResultMsg(code, vehicleId)));

    // ======================================================================
    // Fixed-tick simulation loop
    // ======================================================================

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _tickTimer += dt;
        float tickInterval = 1f / GameConfig.ServerTickHz;
        while (_tickTimer >= tickInterval)
        {
            _tickTimer -= tickInterval;
            try
            {
                _players.TickVehicles(tickInterval);
                _campaign.Tick(tickInterval);
                _economy.Tick(tickInterval);
                foreach (var id in _ai.Tick(tickInterval))
                    _integrity.Log("info", "ai_destroy", $"vehicle {id} destroyed by base AI");
            }
            catch (System.Exception e)
            {
                // A bug in one system must not take down the whole tick loop.
                GD.PushError($"Server tick error: {e}");
            }
        }

        _worldSnapTimer += dt;
        if (_worldSnapTimer >= 1f / GameConfig.WorldSnapshotHz)
        {
            _worldSnapTimer = 0f;
            Multiplayer.Rpc(0, this, "WorldSnapshot", OneArg(BuildWorldSnapshot()));
        }

        _vehicleSnapTimer += dt;
        if (_vehicleSnapTimer >= 1f / GameConfig.VehicleSnapshotHz)
        {
            _vehicleSnapTimer = 0f;
            Multiplayer.Rpc(0, this, "VehicleSnapshot",
                OneArg(NetProtocol.VehicleSnapshotMsg(_vehicles.BuildSnapshotArray())));
        }

        _saveTimer += dt;
        if (_saveTimer >= SaveInterval) { _saveTimer = 0f; _persistence.Save(_world.ToJson()); }
    }

    private Dictionary BuildWorldSnapshot()
    {
        var factions = new Godot.Collections.Array();
        foreach (var f in _world.Factions) factions.Add(NetProtocolHelpers.FactionEntry(f));
        var bases = new Godot.Collections.Array();
        foreach (var b in _world.Bases.Values) bases.Add(NetProtocolHelpers.BaseEntry(b));
        return NetProtocol.WorldSnapshotMsg(factions, bases, _vehicles.BuildSnapshotArray());
    }
}
