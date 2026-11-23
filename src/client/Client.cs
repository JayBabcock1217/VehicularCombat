using Godot;
using Godot.Collections;
using System.Collections.Generic;
using VehicularCombat.Shared;
using VehicularCombat.Shared.Components;

namespace VehicularCombat.Client;

/// <summary>
/// The client root. Connects to the dedicated server, receives world/vehicle
/// snapshots, runs local prediction on its own vehicle, and sends input.
///
/// Run: <c>godot res://src/scenes/client.tscn -- --client --host 127.0.0.1 --port 7005</c>
///
/// RPCs mirror the server: this node defines [Rpc] methods for the server's
/// snapshots (WorldSnapshot/VehicleSnapshot/SpawnResult) and calls the server's
/// request methods (Join/SpawnRequest/SendInput/RespecFaction) via
/// <c>Multiplayer.Rpc(1, this, "Name", args)</c> (peer 1 = the server).
///
/// Prediction: a local <see cref="MotionState"/> is stepped each frame with the
/// current input; on each server snapshot for the local vehicle it resets to the
/// authoritative state (Stage 0 reconciliation — a full delay-compensated
/// re-sim is a later refinement).
/// </summary>
public partial class Client : Node
{
    private ENetMultiplayerPeer _peer;
    private InputController _input;
    private bool _connected;
    public bool Connected => _connected;

    private int _localFaction;
    private VehicleClassId _localClass = VehicleClassId.Ground;
    private int _localVehicleId = -1;
    private float _inputTimer;
    // Headless test driver (see --autospawn): sends Join then Spawn without a GUI.
    private bool _autoSpawn;
    private float _autoTimer;
    private bool _autoJoined, _autoSpawned;

    // ======================================================================
    // VEHICLE MODEL — who computes what (the backend contract)
    // ----------------------------------------------------------------------
    //  * REMOTE vehicles (everyone's except ours): the CLIENT does NO
    //    calculation. It renders each remote vehicle purely from the transform
    //    the server sends every tick (VehicleSnapshot), with light interpolation
    //    between the last two received transforms for smoothness.
    //  * LOCAL vehicle (ours): the CLIENT computes it. It integrates the player's
    //    own inputs every frame (local prediction, so controls feel instant) and
    //    RECONCILES toward the server's authoritative transform when it arrives
    //    (soft correction — no hard reset — so there's no snapping). The server
    //    still includes our vehicle in the broadcast; we simply ignore its
    //    transform for rendering (we own it) and use it only to reconcile.
    // ======================================================================

    // Local prediction state for OUR vehicle only.
    private MotionState _localMotion = new();
    private VehicleClassDef? _localDef;     // our class def (for the prediction step)
    private bool _localMotionReady;          // true once we've received our first transform
    private bool _reconcilePending;          // a server transform arrived; correct on next step
    private Vector3 _reconcilePos;           // the server transform to reconcile toward
    private Node3D _localVisual;             // our vehicle's render node (scene root or stand-in)

    // Rendering (client-only; the server is headless).
    private Node3D _vehicleRoot;
    private readonly System.Collections.Generic.Dictionary<int, Node3D> _vehicleVisuals = new();
    private WorldVisual _worldVisual;
    private CameraRig _cameraRig;

    // Build stamp: printed at startup so you can confirm you're running the
    // LATEST code (a stale client process will show an older number). Bump this
    // on any camera/input/motion change.
    public const string BuildStamp = "VC-2026-11-22b"; // 2026-11-22: CameraRig.SetTarget only re-snaps when the target CHANGES — the per-snapshot (20-25 Hz) camera reset was the remaining fall stutter

    // World mirror (from snapshots).
    public System.Collections.Generic.Dictionary<int, FactionDef> Factions { get; } = new();
    public System.Collections.Generic.Dictionary<string, BaseDef> Bases { get; } = new();
    public System.Collections.Generic.Dictionary<int, RemoteVehicle> Vehicles { get; } = new();

    public class RemoteVehicle
    {
        public int Id;
        public VehicleClassId Class;
        public int Faction;
        public Vector3 Pos;
        public Vector3 Rot;
        public bool Alive;
    }

    public override void _Ready()
    {
        GD.Print($"=== VehicularCombat CLIENT  [build {BuildStamp}] ===");
        _input = new InputController { Name = "InputController" };
        AddChild(_input);
        _vehicleRoot = new Node3D { Name = "VehicleRoot" };
        AddChild(_vehicleRoot);

        // Rendering (client-only). The server is headless, so all visuals are
        // built here from the mirrored snapshot data.
        _worldVisual = new WorldVisual { Name = "WorldVisual" };
        AddChild(_worldVisual);
        _cameraRig = new CameraRig { Name = "CameraRig" };
        AddChild(_cameraRig);
        _cameraRig.Setup(_input, null); // no vehicle yet; idles in overview

        // Set up the HUD (builds its labels). It starts hidden in the scene and is
        // Show()n on spawn (see SpawnResult). Without this call the HUD's labels
        // are never created and nothing renders.
        var hud = GetNodeOrNull<Hud>("Hud");
        if (hud != null)
            hud.Setup(this, 0, VehicleClassId.Ground); // faction/class refined on Join/Spawn

        // Register the vehicle → scene mappings (each plane type is a scene whose
        // root is an AirplaneRig with the model + components dropped in). Classes
        // with no registered scene fall back to the procedural VehicleVisual.
        VehicleSceneRegistry.RegisterDefaults();

        int port = GameConfig.GetPort();
        string host = GameConfig.GetHost();
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateClient(host, port);
        if (err != Error.Ok)
        {
            GD.PushError($"Client: ENet CreateClient({host}:{port}) failed: {err}");
            return;
        }
        Multiplayer.MultiplayerPeer = _peer;
        Multiplayer.ConnectedToServer += () => { _connected = true; GD.Print($"Client: connected to {host}:{port}"); };
        Multiplayer.ConnectionFailed += () => GD.PushError("Client: connection failed");
        Multiplayer.ServerDisconnected += () => { _connected = false; GD.Print("Client: disconnected"); };

        // Start with a VISIBLE, free cursor so the UI (Join/Spawn buttons) is
        // clickable and the pointer is visible. The cursor is hidden only once a
        // vehicle is spawned (see SetCursorForGame). ESC toggles it in InputController.
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // Command-line flags.
        foreach (var a in Godot.OS.GetCmdlineUserArgs())
        {
            if (a == "autospawn")
            {
                _autoSpawn = true;
                GD.Print("Client: --autospawn test driver enabled");
            }
        }
    }

    /// <summary>
    /// Register a specific chosen vehicle's SCENE for its class, so the client
    /// renders that scene for the local vehicle. Called by the connect UI after
    /// the player picks a vehicle (the catalog maps the chosen vehicle's scene,
    /// while <see cref="VehicleSceneRegistry"/> is still class-keyed for the
    /// procedural fallback). A vehicle with no scene keeps the class default.
    /// </summary>
    public void RegisterVehicleScene(VehicleType v)
    {
        if (v == null) return;
        if (!string.IsNullOrEmpty(v.Scene))
            VehicleSceneRegistry.Register(v.Class, v.Scene);
    }

    /// <summary>Enter in-game mouse-look (capture cursor). Delegates to InputController.</summary>
    public void SetCursorForGame()
    {
        _input?.EnterGameMode();
    }

    // NOTE: This class must NOT override _UnhandledInput/_UnhandledKeyInput.
    // Godot routes "unhandled" input to ONE node (the first in the tree that has
    // the virtual and does not consume it). Overriding it here would consume every
    // mouse/keyboard event before the child InputController (mouse-look) ever saw
    // them. All gameplay input handling lives in InputController.

    public override void _Process(double delta)
    {
        if (!_connected) return;
        float dt = (float)delta;

        // Headless test driver: programmatically Join then Spawn (no GUI needed).
        // Uses the registry (like the UI does) rather than hardcoding, so it
        // validates the same data-driven class selection the buttons use.
        if (_autoSpawn)
        {
            _autoTimer += dt;
            if (!_autoJoined && _autoTimer > 1f)
            {
                _autoJoined = true;
                var first = VehicleClassRegistry.FirstClass() ?? (VehicleClassId.Ground, null);
                GD.Print($"[AUTOTEST] sending Join (faction 0, class {first.id})");
                SendJoin(0, first.id);
            }
            if (!_autoSpawned && _autoJoined && _autoTimer > 2.5f)
            {
                _autoSpawned = true;
                string baseId = "alpha_spawn";
                foreach (var kv in Bases) { baseId = kv.Key; break; }
                GD.Print($"[AUTOTEST] sending SpawnRequest (base={baseId}, class={_localClass})");
                SendSpawnRequest(baseId, 0);
            }
        }

        // NOTE: only the camera (mouse-look) is live now; WASD movement + LMB firing
        // controls have been removed, so BuildInputMsg sends zeros for those.

        // Send input at InputSendHz (peer 1 = server) via the SendInput [Rpc]
        // method, which sends from THIS node (path "Server/Client") matching the
        // server's root node path.
        _inputTimer += dt;
        if (_inputTimer >= 1f / GameConfig.InputSendHz)
        {
            _inputTimer = 0f;
            if (_localVehicleId >= 0)
                SendInput(_input.BuildInputMsg());
        }

        // --- LOCAL vehicle: the client computes it (prediction + reconcile). ---
        // Two paths:
        //  * RIGID-BODY path (the new FlightModel + RigidBody3D root): the body
        //    integrates itself each physics frame (thrust/drag/gravity); we only
        //    push the player's throttle into the FlightModel and reconcile the body
        //    toward the server transform when a snapshot arrives.
        //  * KINEMATIC path (MotionState.Step): the legacy stand-in / non-rigid
        //    fallback, kept for any local vehicle that is NOT a rigid body.
        if (_localVehicleId >= 0 && _localVisual != null && _localMotionReady)
        {
            var flightModel = _localVisual.GetNodeOrNull<FlightModel>("FlightModel");

            if (flightModel != null)
            {
                // WAR-THUNDER MODEL: the local vehicle is a PURE CLIENT simulation.
                // The rigid body integrates thrust/drag/gravity every physics frame
                // and the chase camera reads the body DIRECTLY. The server's
                // snapshot for OUR vehicle is used ONLY to seed once on spawn.
                // After that, the client sim owns the vehicle — zero server
                // influence in steady state = smooth camera.
                // We must not set any properties that would interfere with the 
                // FlightModel's own position updates.
                flightModel.ThrottleTarget = _input != null ? _input.Throttle : 0f;
            }
            else if (_localDef != null)
            {
                // Kinematic fallback (legacy stand-in, no rigid body / flight model).
                // Note: For the client-side physics to be stable and avoid jitter,
                // we should NOT reconcile here. The local prediction should be the
                // source of truth and we should NOT update from server snapshots 
                // for our own vehicle.
                _localMotion.Step(dt, 0f, 0f, 0f, _localDef);
                _localVisual.Position = _localMotion.Position;
                _localVisual.Rotation = _localMotion.Rotation;
            }
        }

        // --- REMOTE vehicles: the client does NOT compute them. It renders each
        // --- purely from the transforms the server sent, with light
        // --- interpolation between the last two received transforms.
        SyncRemoteVehicles();

        // Keep the base markers in sync with the mirrored world data.
        _worldVisual?.UpdateBases(Bases, Factions);
    }

    private static Godot.Collections.Array OneArg(Dictionary d)
    {
        var a = new Godot.Collections.Array();
        a.Add(d);
        return a;
    }

    // ======================================================================
    // Server -> Client RPCs (server calls; this node receives + applies)
    // ======================================================================

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void WorldSnapshot(Variant msg)
    {
        var m = (Dictionary)msg;
        foreach (var fe in (Godot.Collections.Array)m["factions"])
        {
            var e = (Dictionary)fe;
            int id = (int)e["id"];
            if (!Factions.TryGetValue(id, out var f)) { f = new FactionDef(); Factions[id] = f; }
            f.Id = id;
            f.Name = (string)e["name"];
            var c = (float[])e["color"];
            f.Color = new Color(c[0], c[1], c[2], c[3]);
            f.ResourcePool = (float)e["pool"];
        }
        foreach (var be in (Godot.Collections.Array)m["bases"])
        {
            var e = (Dictionary)be;
            string id = (string)e["id"];
            if (!Bases.TryGetValue(id, out var b)) { b = new BaseDef(); Bases[id] = b; }
            b.Id = id;
            b.OwnerFaction = (int)e["owner"];
            b.CaptureProgress = (float)e["capture_progress"];
            b.CapturingFaction = (int)e["capturing"];
        }
        ApplyVehicles((Godot.Collections.Array)m["vehicles"]);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void VehicleSnapshot(Variant msg)
        => ApplyVehicles((Godot.Collections.Array)((Dictionary)msg)["vehicles"]);

    // Request RPCs: declared on THIS node so (a) the node's [Rpc] method set
    // (names AND RpcMode) matches the server root (satisfying the per-node RPC
    // checksum) and (b) the outgoing RPC config resolves when we call them.
    // These are the real senders; the server's Server/Client node carries the
    // actual handlers. Variant param + RpcMode.AnyPeer are both required (see
    // Server.cs notes) for the method to register and be accepted.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void Join(Variant msg)
        => Multiplayer.Rpc(1, this, "Join", OneArg((Dictionary)msg));
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void SpawnRequest(Variant msg)
        => Multiplayer.Rpc(1, this, "SpawnRequest", OneArg((Dictionary)msg));
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void SendInput(Variant msg)
        => Multiplayer.Rpc(1, this, "SendInput", OneArg((Dictionary)msg));
    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void RespecFaction(Variant msg)
        => Multiplayer.Rpc(1, this, "RespecFaction", OneArg((Dictionary)msg));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    public void SpawnResult(Variant msg)
    {
        var m = (Dictionary)msg;
        int code = (int)m["code"];
        int vehicleId = (int)m["vehicle_id"];
        if (code == NetProtocol.SpawnOk)
        {
            _localVehicleId = vehicleId;
            _localMotionReady = false;   // seed our prediction from the first server transform
            _reconcilePending = false;
            GD.Print($"Client: spawned vehicle {vehicleId}");
            var ui = GetNodeOrNull<ConnectUi>("ConnectUi");
            ui?.Hide();
            var hud = GetNodeOrNull<Hud>("Hud");
            hud?.Show();
            SetCursorForGame(); // capture cursor for mouse-look once in a vehicle
            GD.Print($"Client: in vehicle {vehicleId} — mouse-look active. ESC = release cursor, WASD move, mouse orbits 360°.");
        }
        else
        {
            GD.PushWarning($"Client: spawn rejected (code {code})");
        }
    }

    private void ApplyVehicles(Godot.Collections.Array arr)
    {
        var seen = new HashSet<int>();
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var ve in arr)
        {
            var e = (Dictionary)ve;
            int id = (int)e["id"];
            seen.Add(id);
            if (!Vehicles.TryGetValue(id, out var rv)) { rv = new RemoteVehicle { Id = id }; Vehicles[id] = rv; }
            rv.Class = (VehicleClassId)(int)e["class"];
            rv.Faction = (int)e["faction"];
            var p = (float[])e["pos"];
            rv.Pos = new Vector3(p[0], p[1], p[2]);
            if (e.ContainsKey("rot"))
            {
                var r = (float[])e["rot"];
                rv.Rot = new Vector3(r[0], r[1], r[2]);
            }
            rv.Alive = (bool)e["alive"];

            // Ensure a rendered node exists for this vehicle.
            //  * LOCAL: the registered scene (AirplaneRig + components).
            //  * REMOTE: a lightweight procedural stand-in.
            if (!_vehicleVisuals.TryGetValue(id, out var vis))
            {
                vis = id == _localVehicleId
                    ? BuildLocalVehicleNode(rv.Class)
                    : BuildStandIn(rv.Class, rv.Faction);
                vis.Name = "Vehicle_" + id;
                _vehicleRoot.AddChild(vis);
                _vehicleVisuals[id] = vis;
            }

            if (id == _localVehicleId)
            {
                // WE OWN this vehicle: the client computes it (prediction), so we
                // do NOT render it from the server transform. We use the server
                // transform ONLY to seed our prediction once (on spawn). After
                // that, the client's local simulation (rigid body / kinematic) is
                // the truth. No per-tick reconciliation — that was the jitter source.
                _localVisual = vis;
                _localDef = VehicleClassRegistry.Get(rv.Class);
                _cameraRig?.SetTarget(vis);
                if (!_localMotionReady)
                {
                    // First transform for our vehicle: seed the prediction state
                    // from the server's authoritative transform, then start.
                    _localMotion.SetState(rv.Pos, rv.Rot, Vector3.Zero);
                    _localVisual.Position = rv.Pos;
                    _localVisual.Rotation = rv.Rot;
                    _localMotionReady = true;
                }
                // Steady state: do NOTHING. The local sim owns this vehicle.
                // (The server's transform for our vehicle is ignored.)
                // Note: We do NOT perform reconciliation here for our own vehicle
                // to avoid jitter. The local prediction is the source of truth.
            }
            else
            {
                // REMOTE vehicle: shift the interpolation buffers so
                // SyncRemoteVehicles can lerp the render transform between the
                // last two server transforms. The client does NO simulation here.
                ShiftRemoteInterp(id, rv.Pos, rv.Rot, now);
            }
        }
        // Remove vehicles that left the world (destroyed / player left).
        foreach (var id in new List<int>(Vehicles.Keys))
        {
            if (!seen.Contains(id))
            {
                Vehicles.Remove(id);
                _remoteInterp.Remove(id);
                if (_vehicleVisuals.TryGetValue(id, out var vis))
                {
                    vis.QueueFree();
                    _vehicleVisuals.Remove(id);
                    if (id == _localVehicleId) _localVisual = null;
                }
            }
        }
    }

    // Per-remote-vehicle interpolation state (render smoothing only — NOT simulation).
    private class RemoteInterp { public Vector3 PrevPos, CurrPos, PrevRot, CurrRot; public double PrevTime, CurrTime; public bool HasPrev; }
    private readonly System.Collections.Generic.Dictionary<int, RemoteInterp> _remoteInterp = new();

    private void ShiftRemoteInterp(int id, Vector3 pos, Vector3 rot, double now)
    {
        if (!_remoteInterp.TryGetValue(id, out var ri)) { ri = new RemoteInterp { CurrPos = pos, CurrRot = rot, CurrTime = now }; _remoteInterp[id] = ri; return; }
        ri.PrevPos = ri.CurrPos; ri.PrevRot = ri.CurrRot; ri.PrevTime = ri.CurrTime;
        ri.CurrPos = pos; ri.CurrRot = rot; ri.CurrTime = now;
        ri.HasPrev = true;
    }

    /// <summary>
    /// Render every REMOTE vehicle purely from the transforms the server sent,
    /// interpolating between the last two for smoothness. The client performs no
    /// motion calculation for these — it only lerp the received transforms.
    /// </summary>
    private void SyncRemoteVehicles()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var kv in _remoteInterp)
        {
            int id = kv.Key;
            var ri = kv.Value;
            if (!_vehicleVisuals.TryGetValue(id, out var vis)) continue;
            if (!ri.HasPrev)
            {
                vis.Position = ri.CurrPos;
                if (ri.CurrRot != Vector3.Zero) vis.Rotation = ri.CurrRot;
                continue;
            }
            double span = ri.CurrTime - ri.PrevTime;
            if (span <= 0.0001)
            {
                vis.Position = ri.CurrPos;
                continue;
            }
            // Render ~40 ms behind the latest transform for a stable window.
            double t = (now - 0.04 - ri.PrevTime) / span;
            t = Mathf.Clamp(t, 0f, 1f);
            vis.Position = ri.PrevPos.Lerp(ri.CurrPos, (float)t);
            vis.Rotation = ri.PrevRot.Lerp(ri.CurrRot, (float)t);
        }
    }

    /// <summary>
    /// Build the node to render the LOCAL vehicle: instantiate its registered
    /// scene (an <c>AirplaneRig</c> with the model + components), or fall back to
    /// a procedural stand-in if no scene is registered for the class.
    /// </summary>
    private Node3D BuildLocalVehicleNode(VehicleClassId cls)
    {
        var scene = VehicleSceneRegistry.GetScene(cls);
        if (scene != null)
        {
            var root = scene.Instantiate<Node3D>();
            GD.Print($"Client: instantiated vehicle scene for {cls}: {VehicleSceneRegistry.HasScene(cls)}");
            return root;
        }
        return BuildStandIn(cls, -1);
    }

    /// <summary>Build a lightweight procedural stand-in for a vehicle (used for
    /// remote vehicles and as the no-scene fallback).</summary>
    private Node3D BuildStandIn(VehicleClassId cls, int faction)
    {
        var vis = new VehicleVisual();
        vis.Build(cls, faction);
        return vis;
    }

    /// <summary>Record the local vehicle class (set on spawn). Used for input context.</summary>
    public void SetLocalClass(VehicleClassId cls) => _localClass = cls;

    // --- Accessors for the HUD (frame-accurate read of the local vehicle) -----

    /// <summary>The local vehicle's render node (rigid body / stand-in), or null.</summary>
    public Node3D? LocalVehicle => _localVisual;

    /// <summary>The local vehicle's flight model (for throttle/thrust), or null.</summary>
    public FlightModel? LocalFlightModel =>
        _localVisual != null ? _localVisual.GetNodeOrNull<FlightModel>("FlightModel") : null;

    /// <summary>The current player throttle 0..1 (from input), for the HUD.</summary>
    public float LocalThrottle => _input != null ? _input.Throttle : 0f;

    /// <summary>The local vehicle's current velocity (m/s), or zero.</summary>
    public Vector3 LocalVelocity
    {
        get
        {
            // Prefer the FlightModel's integrated velocity (custom kinematic sim).
            var fm = _localVisual != null ? _localVisual.GetNodeOrNull<FlightModel>("FlightModel") : null;
            if (fm != null) return fm.Velocity;
            if (_localVisual is RigidBody3D rb) return rb.LinearVelocity;
            return _localMotion.Velocity;
        }
    }

    /// <summary>The local vehicle's world position, or zero.</summary>
    public Vector3 LocalPosition => _localVisual != null ? _localVisual.GlobalPosition : _localMotion.Position;

    // ======================================================================
    // Actions the UI / player triggers. Request RPCs are sent from THIS node
    // (path "Server/Client") to the server (peer 1). This node's path matches
    // the server's root node, and this node declares every [Rpc] method the
    // server root does (Join/SpawnRequest/SendInput/RespecFaction/
    // WorldSnapshot/VehicleSnapshot/SpawnResult), satisfying Godot's per-node
    // RPC checksum.
    // ======================================================================

    public void SendJoin(int faction, VehicleClassId cls)
    {
        _localFaction = faction;
        _localClass = cls;
        // Refresh the HUD with the real faction/class (it was set up with defaults
        // at startup). Re-running Setup is cheap and idempotent (labels are created
        // once; the values just update).
        GetNodeOrNull<Hud>("Hud")?.Setup(this, faction, cls);
        Join(NetProtocol.JoinMsg(faction, cls)); // [Rpc] method -> sends to server
    }

    public void SendSpawnRequest(string baseId, int slot)
        => SpawnRequest(NetProtocol.SpawnRequestMsg(baseId, _localClass, slot));
}
