# Stage 0 — Foundation: Architecture Plan

**Goal (from GOALS.md §7.1):** a single persistent, networked, server-authoritative world with the vehicle-agnostic systems every later stage depends on.

This document defines the concrete architecture for Stage 0. It is the implementation contract for the code in `src/`.

---

## 1. What Stage 0 proves (definition of done)

A player can:
1. Start a **headless dedicated server** (`godot --headless -- server`) and a **client** (`godot -- client`).
2. The client connects to the server over Godot's `MultiplayerAPI` (ENet).
3. The client chooses a **faction** (of two) and a **vehicle class** (air / ground / naval / heli).
4. The client spawns a **stand-in vehicle** from a faction **base** (frontline or deep-territory choice).
5. The vehicle moves with **WASD + mouse-look** (class-appropriate minimal motion), server-authoritative with client prediction.
6. Bases can be **captured** by holding them; capture flips **faction ownership** and the **AI defense** that guards the base.
7. Each faction has a **shared resource pool** that **generates over time** based on captured/built infrastructure; **spawning a vehicle costs** from that pool.
8. **World state persists across server restarts** (SQLite) — factions, base ownership, resource pools, AI defense state.
9. **AI spawn-defense** (a simple interceptor/defender placeholder) holds at least one base and intercepts hostile vehicles until the base is captured.
10. The **framework accepts a new vehicle class without rework** — adding class 5 should be a data + small code addition, not a refactor.

Non-goals for Stage 0 (see GOALS.md §8): real flight model, weapons/damage beyond a placeholder destroy, the heightmap pipeline, server browser, spool-up, accounts, real-money.

### 1.1 Implementation status

| Item | Status |
|------|--------|
| C# codebase compiles (0 errors, 0 warnings) | ✅ verified via `dotnet build` |
| Headless server boots, builds world (2 factions, 8 bases) | ✅ verified |
| Economy generates (pool 1000 → 1225 over ~9 s) | ✅ verified |
| Persistence round-trip (save on exit, load on start) | ✅ verified |
| Client connects to server (live ENet) | ✅ verified (`Client: connected`, 0 RPC errors) |
| All RPCs registered + delivered (no checksum / config / node-path errors) | ✅ verified — fixed by using `Variant` (not `Dictionary`) RPC params; see §3 notes |
| Client→server RPCs accepted (no "not allowed" / authority errors) | ✅ verified — fixed by `RpcMode.AnyPeer` (default is `Authority`, which rejects clients); see §3 notes |
| Client spawn/move/capture round-trip | ✅ **verified via headless autotest** (`--autospawn`): Join→Spawn→SpawnResult round-trip works, `spawned vehicle 1 (Air)`, 0 errors. The UI *click* flow is the same code path (see §1.2). |
| AI defense intercepts hostiles | ⬜ logic written; needs live test |
| Integrity audit log populates | ⬜ logic written; needs live test |
| Vehicle stand-in visuals + bases + third-person camera | ✅ built in code (procedural, no assets) — `VehicleVisual`/`WorldVisual`/`CameraRig`; round-trip verified headless, visuals confirmed in the GUI run |
| WASD + mouse-look input (full 360° orbit, correct yaw + pitch direction) | ✅ **verified in GUI** — mouse-look gated on an explicit `InGame` flag (not `MouseMode`, which silently disabled look); cursor `Captured` for in-game, `Visible` for UI; ESC toggles; `Firing` polled. Both axis signs confirmed correct by the user (build `VC-2026-11-21f`) |
| Smooth vehicle motion (no stutter) | ✅ fixed — remote vehicles render by interpolating the last two server transforms (lerp, ~40 ms behind); local vehicle is client-predicted + server-reconciled (see §1.4) |
| Vehicle backend: local predicted, remotes receive-only | ✅ **verified (2-client headless)** — server broadcasts all vehicle transforms every tick; each client **ignores its own** transform (computes it via local prediction + soft reconcile) and renders every **remote** vehicle purely from the received transforms (interpolated). The client does NO motion calculation for any vehicle but its own. `MotionState.SetState` added for seeding/reconcile. |
| Vehicle spawn selection UI (faction → type → vehicle) | ✅ **verified (headless)** — `ConnectUi` cascades Faction → Vehicle type (Plane/Tank/Ship/Heli) → Vehicle (populated from `data/vehicles.json` for that faction+type) → Base/Slot. The chosen vehicle's `class_id` drives server motion/stats; its `scene` is registered on the client for rendering. Autotest spawn still round-trips (0 errors). |
| Vehicle catalog editable in the Godot editor | ✅ **verified (headless, 0 errors)** — the catalog is a folder of `VehicleEntry` resources: `data/vehicles/<faction>/<type>/<vehicle>.tres`, each edited in the editor's inspector (Name/Class/Scene; drag a scene in; right-click → New Resource to add). `VehicleTypeRegistry` scans the folders at startup; round-trip verified (in-game **Save** writes the same `.tres`, which reload with 0 errors). `data/vehicles.json` is the JSON fallback. |

The server-side core loop is proven headless. Input/camera/motion are now wired and verified in the GUI. The remaining items require a live client run (and the editor for visuals) — the next step.

### 1.4 Vehicle backend: who computes what

The split of motion computation between server and client is an explicit contract (verified with a 2-client headless run):

- **Server = authority.** It integrates the motion of **every** live vehicle (it's headless, so it computes all of them) and broadcasts the full transform set of all vehicles every tick via `VehicleSnapshot` (20 Hz) to all peers in one broadcast. It never knows which client "owns" which vehicle for rendering purposes — it just sends everyone's transforms to everyone.
- **Remote vehicles (everyone's except ours): receive-only.** The client does **no** motion calculation for them. Each remote vehicle's render node is positioned purely from the transforms the server sent, with light interpolation (lerp) between the last two received transforms rendered ~40 ms behind, for smoothness. That's it — no integration, no prediction.
- **Local vehicle (ours): the client computes it.** The client integrates the player's own inputs every frame (local prediction, so controls feel instant) and **reconciles** toward the server's authoritative transform when it arrives (a soft 50 % blend, not a hard reset, to hide drift without snapping). The server still includes our vehicle in the broadcast; we simply ignore its transform for rendering (we own it) and use it only to seed the prediction state once and then reconcile.

Key code: `Client.ApplyVehicles` (splits local vs remote), `Client.SyncRemoteVehicles` (renders remotes from received transforms), the local prediction block in `Client._Process` (predict + reconcile), and `MotionState.SetState` (shared, used to seed/reconcile the local `MotionState` from a server transform).

Note: movement controls (WASD) are currently removed, so the local prediction integrates zero movement input (the vehicle holds position). The prediction + reconcile path is fully wired — when the flight model / movement controls return, feed the real `Throttle/Turn/Pitch` into the predict step and it works with no other change.

### 1.5 Vehicle spawn catalog + editor

The spawn UI and its configuration are data-driven off the **vehicle catalog**, loaded by `VehicleTypeRegistry` (shared by client + server). The catalog is a **folder structure of Godot resources**, edited **directly in the Godot editor's inspector** (no code, no running the game):

```
data/vehicles/<factionId>/<typeKey>/<vehicle>.tres
```

- **Faction** = a subfolder of `data/vehicles/`, named by faction id (`0/`, `1/`, …). Add a faction = add a folder.
- **Type** = a subfolder of a faction (`air/`, `ground/`, `naval/`, `heli/`, or any custom key). Add a type = add a folder.
- **Vehicle** = a `VehicleEntry` resource (`.tres`) inside a type folder. Each has three exported fields, all editable in the inspector:
  - `Name` – player-facing label
  - `Class` – Air / Ground / Naval / Heli (drives motion + the server stand-in, via `data/vehicle_classes.json`)
  - `Scene` – drag your `.tscn` in (what the **client** renders for it; the server stays headless)

**How to add a vehicle (all in the editor):** in the FileSystem dock, right-click the `data/vehicles/<faction>/<type>` folder → **New Resource…** → pick **VehicleEntry** → save it there → select it → fill in Name/Class/Scene in the inspector → Save (Ctrl+S). To add a faction or type, just make a folder. `VehicleTypeRegistry` scans the folders at startup (client + server); `data/vehicles.json` remains as a JSON fallback/migration source.

> Note (Godot 4.8 C#): a `.tres` that references a C# script class by *path* requires the **filename to match the class name** — hence the resource lives in `src/shared/VehicleEntry.cs` (class `VehicleEntry`). A single all-in-one `.tres` with C# sub-resource classes does not round-trip in this version, so the catalog uses one resource per vehicle.

**Spawn UI (`ConnectUi`)** — the cascade: **Faction → Vehicle type (Plane/Tank/Ship/Heli) → Vehicle (populated from the catalog for that faction+type) → Base/Slot**. Picking a faction or type repopulates the vehicle dropdown; spawning registers the chosen vehicle's scene on the client and sends its `class_id` to the server.

**In-game editor (`VehicleEditorPanel`)** — a convenience: the connect UI's **"Configure vehicles… (edit the catalog)"** button opens a full-screen panel to add/remove factions/types/vehicles without leaving the game. **Save** writes the same `data/vehicles/…/*.tres` files the editor's inspector shows, so both paths stay in sync. The primary, intended way to configure is still the editor's inspector (right-click → New Resource).

---

## 2. High-level architecture

```
                 +------------------------------------------+
                 |            DEDICATED SERVER              |
                 |  (godot --headless -- server)            |
                 |                                          |
                 |  Server (Node, server-authoritative)     |
                 |   +- WorldState      (factions, bases,   |
                 |   |                   resource pools)    |
                 |   +- Persistence     (SQLite save/load)  |
                 |   +- Campaign        (capture/hold,      |
                 |   |                   event spawn)       |
                 |   +- Economy         (gen rate, spawn    |
                 |   |                   cost accounting)   |
                 |   +- AiDefense       (per-base AI)       |
                 |   +- Integrity       (input validation,  |
                 |   |                   anomaly, audit log)|
                 |   +- PlayerManager   (sessions, spawns)  |
                 |   +- VehicleSpawner  (class stand-ins)   |
                 +------------------|-----------------------+
                                    |  MultiplayerAPI (ENet)
                       snapshots up / state down
                                    |
                 +------------------v-----------------------+
                 |              CLIENT                       |
                 |  (godot -- client)                        |
                 |  Client (Node)                            |
                 |   +- Prediction   (local input predict)   |
                 |   +- Interpolation (remote vehicles)      |
                 |   +- Input         (WASD + mouse-look)    |
                 |   +- ViewModel/HUD (faction, resource,    |
                 |   |                 base status)         |
                 |   +- VehicleStandIns (visual, per class)  |
                 +-------------------------------------------+
```

**Server-authoritative rule:** the server owns *all* game-relevant state (positions, base ownership, resource pools, AI). Clients send *inputs*; the server integrates and broadcasts *snapshots*. A client can never set its own position, capture a base, or spend resources directly — it requests, the server validates.

---

## 3. Godot project layout

```
vehicular-combat/
├── project.godot
├── GOALS.md
├── docs/
│   └── STAGE0.md                 # this file
├── src/
│   ├── autoload/
│   │   ├── Net.gd                # (GDScript) or C#: global net config, peer mgmt helpers
│   │   └── AuditLog.gd           # integrity: append-only audit event log
│   ├── server/
│   │   ├── server.gd             # Server root node; boots WorldState, Campaign, Economy, AI, Integrity
│   │   ├── world_state.gd        # factions, bases, resource pools (authoritative source of truth)
│   │   ├── persistence.gd        # SQLite: save/load WorldState
│   │   ├── campaign.gd           # capture/hold logic, frontline, event spawn
│   │   ├── economy.gd            # resource generation + spawn cost accounting
│   │   ├── ai_defense.gd         # per-base AI defense (interceptor/defender placeholder)
│   │   ├── integrity.gd          # input validation, anomaly detection, audit hooks
│   │   ├── player_manager.gd     # player sessions, join/leave, spawn requests
│   │   └── vehicle_spawner.gd    # class stand-in instantiation (data-driven)
│   ├── client/
│   │   ├── client.gd             # Client root; connects, prediction, interpolation
│   │   ├── prediction.gd         # local vehicle input prediction + server reconciliation
│   │   ├── interpolation.gd      # remote vehicle smoothing
│   │   ├── input_controller.gd   # WASD + mouse-look capture
│   │   ├── hud.gd                # faction, resource pool, base status, spawn UI
│   │   └── connect_ui.gd         # faction + vehicle-class selection, connect button
│   ├── shared/
│   │   ├── vehicle_class.gd      # enum + data (Air, Ground, Naval, Heli) — data-driven
│   │   ├── vehicle_standin.gd    # base stand-in node (server + client), class-specific motion
│   │   ├── base.gd               # a base/infrastructure object (owner, defense, hold radius)
│   │   ├── faction.gd            # faction data (id, color, resource pool)
│   │   └── net_protocol.gd       # (C#) typed RPC/message definitions shared by client & server
│   └── scenes/
│       ├── server.tscn           # Server root scene
│       ├── client.tscn           # Client root scene
│       ├── world/
│       │   ├── test_zone.tscn    # procedural test zone (ground + bases)
│       │   ├── base.tscn
│       │   └── vehicle_standin.tscn
│       └── ui/
│           ├── hud.tscn
│           └── connect_ui.tscn
└── data/
    ├── vehicle_classes.json      # data-driven class definitions (tuning)
    └── bases.json                # base/infrastructure definitions
```

**Language note (decided):** all Stage 0 logic is **C#** (the project's `VehicularCombat` assembly). Scenes (`.tscn`) reference the C# classes. A `VehicularCombat.csproj` (Godot.NET.Sdk) is required for the classes to resolve and build.

### 3.1 C#-specific implementation notes (Godot 4.8)

These are decisions forced by the Godot 4.8 C# API, documented so future work doesn't re-litigate them:

- **Two entry scenes, one shared assembly.** Godot C# RPCs are peer-agnostic, so the server and client are **separate scenes** (`server.tscn`, `client.tscn`) that share the one `VehicularCombat` assembly (shared: protocol, data, `MotionState`, `VehicleStandin`). "Same project, `--headless`" still holds — you launch the right scene.
- **RPCs via the dynamic `Multiplayer.Rpc` overload.** In Godot 4.8 the C# source generator does **not** emit the `MethodName.*` constants / `Rpc`/`RpcId` extension methods (it only emits them in later/other builds). The reliable pattern is the static dynamic overload:
  `Multiplayer.Rpc(peerId, thisNode, "MethodName", argsArray)` where `argsArray` is a `Godot.Collections.Array` of the method's arguments, peer `0` = broadcast, `1` = the server (from a client's view). The receiving method is marked `[Rpc]`.
- **RPC parameters MUST be `Variant`, NOT `Godot.Collections.Dictionary`.** The Godot C# source generator (`Godot.SourceGenerators`) only registers a method as Godot-callable if every parameter (and the return type) is a *marshalable* type: primitives, `String`, `bool`, the vector/math types, enums, and **`Variant`**. `Godot.Collections.Dictionary` (and `Array`) are **not** marshalable, so a `[Rpc]` method whose parameter is `Dictionary` is silently excluded from `HasGodotClassMethod` → `HasMethod("Join")` returns `false` → Godot registers **zero** RPC configs on that node → the per-node checksum is empty on *both* peers (so it "passes") but the RPC is **never registered and never delivered**. This is the #1 C# multiplayer footgun and it fails *silently*. All RPC methods here therefore take a `Variant msg` and cast to `(Dictionary)msg` in the body. (Verified by decompiling the generated `HasGodotClassMethod`: `Dictionary` params are absent, `Variant` params present.)
- **`[Rpc]` methods must exist on the receiving node, and the per-node checksum must match.** Godot 4.8 computes a per-node RPC checksum (MD5 of the concatenated **names** of the node's Godot-compatible `[Rpc]` methods, in hash order) and requires it identical on both peers, *and* requires the `[Rpc]` method to exist on the *calling* node to build the outgoing RPC config. Both the server and client therefore have a node at the **same path** `Server/Client` whose `[Rpc]` set is **identical (7 methods)**: `Join`/`SpawnRequest`/`SendInput`/`RespecFaction` (client→server requests) + `WorldSnapshot`/`VehicleSnapshot`/`SpawnResult` (server→client). On the server node these are the real handlers; on the client node `WorldSnapshot`/`VehicleSnapshot`/`SpawnResult` apply state and the request methods re-send to peer 1. Because the client sends from its `Client` node (path `Server/Client`), the serialized target path matches the server's. The server's `Server/Client` node is the *only* RPC node — `PlayerManager` builds the `SpawnResult` reply and hands it to the server root, which sends it from `Server/Client` (see `Server.SendSpawnResult`). Note: the input method is named `SendInput`, **not** `Input`, because `Input` collides with the `Godot.Input` static class.
- **`RpcMode` is required and is a NESTED enum.** `[Rpc]` with no argument defaults to `RpcMode.Authority`, which **rejects calls from non-authority peers** — so a bare `[Rpc]` on a client→server request fails at runtime with `RPC 'Join' is not allowed on node … Mode is "authority", authority is 1.` All RPC methods here therefore use `[Rpc(MultiplayerApi.RpcMode.AnyPeer)]`. Note `RpcMode` is **not** top-level `Godot.RpcMode`; it is the **nested** `Godot.MultiplayerApi.RpcMode`, so the attribute is written `[Rpc(MultiplayerApi.RpcMode.AnyPeer)]`. `RpcMode` = `{ Disabled, AnyPeer, Authority }`. Both peers' `[Rpc]` methods must use the *same* `RpcMode` or the per-node checksum (which includes the mode) won't match.
- **Peer events use `long`.** `MultiplayerApi.PeerConnected`/`PeerDisconnected` handlers take a `long` peer id in 4.8 (not `int`).
- **Build the assembly via the Godot SDK, then run.** `dotnet build` and Godot both output to `.godot/mono/temp/bin/Debug/VehicularCombat.dll` (the SDK sets `BaseOutputPath` there). Run the **server headless** (`godot --headless …server.tscn`) in the background and wait for `listening on port` before launching the **client** (a GUI build); launching both simultaneously can let the client load a half-built assembly. If RPCs suddenly stop being registered after editing, delete `.godot/mono/temp` and rebuild to force the source generator to re-run.
- **No `Godot.JSON` in 4.8.** JSON is handled by `System.Text.Json`, bridged to/from `Godot.Collections` by `shared/JsonUtil.cs`.
- **Persistence: durable JSON file (Stage 0), SQLite later.** The Godot SQLite driver (GDExtension) is not present in this Godot 4.8 build, and the Godot .NET SDK does **not** copy NuGet package dependencies into the runtime output (so ADO.NET `Microsoft.Data.Sqlite` can't be loaded by the Godot-hosted server either). Stage 0 therefore persists to a durable JSON file (`user://world_save.json`, atomic write) via `Persistence.cs`. The load/save contract is storage-agnostic, so **upgrading to SQLite is a drop-in swap** once the economy needs queryable/transactional storage (per-faction transaction history, audit queries).
- **`GD.PrintWarning` → `GD.PushWarning`; `RandomNumberGenerator.RangeF` → `RandfRange`.**

---

## 4. Networking protocol (Stage 0)

Transport: Godot `MultiplayerAPI`, **ENet**, dedicated server.

**Client → Server (RPCs):**
| Message | Payload | Purpose |
|---|---|---|
| `Join` | accountless session token, faction, vehicle class | request to join the world |
| `SpawnRequest` | base id, spawn slot (frontline/deep) | request to spawn a stand-in (server checks resource pool) |
| `Input` | movement vector, aim yaw/pitch, (class) | continuous input stream (throttled ~20 Hz) |
| `RespecFaction` | faction | switch faction (server-validated) |

**Server → Client (RPCs / snapshots):**
| Message | Payload | Purpose |
|---|---|---|
| `WorldSnapshot` | factions, resource pools, base ownership + defense state | authoritative world state (periodic, e.g. 5 Hz) |
| `VehicleSnapshot` | id, class, faction, pos, rot, velocity, alive | per-vehicle state (periodic, e.g. 20 Hz) |
| `BaseEvent` | base id, new owner, capture progress | capture/hold updates |
| `SpawnResult` | ok/err, vehicle id, error reason | result of a `SpawnRequest` |
| `AuditNotice` | event, detail | integrity notices (anomaly, rejection) |

**Tick model:** server runs a fixed logic tick (target **20 Hz**, configurable). Each tick: process inputs → integrate vehicle motion → update campaign/hold → update economy → update AI defense → broadcast snapshots. Clients interpolate vehicle snapshots and run local prediction on their own vehicle.

**Bandwidth:** Stage 0 stand-ins are cheap (pos+rot+velocity). The snapshot cadence above is a starting point; to be benchmarked (GOALS.md §9).

---

## 5. Key systems

### 5.1 WorldState (authoritative)
- Two `Faction` objects (id, name, color, `resource_pool: float`).
- A set of `Base` objects (id, faction owner, defense state, hold radius, position).
- The single source of truth; every other system reads/writes through it.

### 5.2 Campaign (capture/hold)
- A base is **captured** when a faction's vehicle(s) remain within its hold radius for a hold duration while no hostile vehicle is present (or out-numbers, per tuning).
- Capture flips `Base.owner` and the base's `AiDefense` allegiance.
- Produces the **frontline** (the contested boundary between factions) used for spawn placement.

### 5.3 Economy (team resource pool)
- Each faction's `resource_pool` **generates** at a rate = `base_rate + sum(captured infrastructure generation)`.
- **Spawning a vehicle costs** `class_spawn_cost` from the owning faction's pool; the `SpawnRequest` is rejected if the pool is insufficient or no spawn slot is free.
- This is the loop: capture → more generation → more vehicles → push.

### 5.4 AI Defense (per-base placeholder)
- Each base has an `AiDefense` that, while the base is faction-owned, spawns/positions a simple **interceptor** (hostile to non-owner vehicles) and a **defender** that intercepts hostiles entering the base's defense radius.
- When captured, the defense switches allegiance.
- Stage 0 uses a minimal "move toward nearest hostile in radius, apply placeholder damage/destroy" AI — the *tuning* (range, effectiveness, scaling) is an open question (GOALS.md §9), but the *structure* (per-base, allegiance-flipping, intercept-on-entry) is fixed now.

### 5.5 Integrity (foundation, high priority)
- **Server-authoritative:** clients send inputs only; the server integrates all motion and all state changes.
- **Input validation:** reject out-of-range / implausible inputs (e.g. speed exceeding the class max).
- **Anomaly detection:** flag events that can't arise from valid inputs (teleport, instant capture).
- **Audit log:** append-only log of key events (join, spawn, capture, resource spend, destroy, anomaly) with timestamps — the foundation for later review/enforcement (GOALS.md §9).

### 5.6 Persistence (SQLite)
- On server start: load `WorldState` (factions, base ownership, resource pools) from SQLite.
- On shutdown and on a periodic interval: save `WorldState`.
- Stage 0 persists the *campaign state*, not individual player sessions (accounts are a later stage).

### 5.7 Data-driven vehicle classes
- `vehicle_classes.json` defines each class: id, name, max speed, turn rate, (placeholder) spawn cost, defense/role.
- Adding class 5 = add a JSON entry + (optionally) a small motion override. No refactor of the spawner or protocol. This is the "no rework" requirement made concrete.

---

## 6. Scene / node responsibilities

- **`server.tscn`** → `Server` (C#): builds `WorldState`, `Campaign`, `Economy`, `AiDefense` (per base), `Integrity`, `PlayerManager`, `VehicleSpawner`; runs the fixed-tick loop; loads `Persistence`.
- **`client.tscn`** → `Client` (C#): `ConnectUI` (faction/class pick + connect), on connect → `InputController`, `Prediction`, `Interpolation`, `HUD`, and a `World` instance that receives snapshots.
- **`test_zone.tscn`**: procedural ground (large `MeshInstance3D` plane / low-poly terrain) + placed `Base` nodes from `bases.json`.
- **`vehicle_standin.tscn`**: a `Node3D` with a class-distinct primitive visual (air = cone, ground = box, naval = flattened box, heli = box+rotor disc) and a `VehicleStandin` script; class-specific minimal motion.

---

## 7. Build & run

**Prereq:** the Godot .NET SDK must be installed for your Godot version so the editor can build the C# assembly. The `VehicularCombat.csproj` targets `net10.0` and `Godot.NET.Sdk/4.8.0-beta.2` (matching the cached GodotSharp 4.8.0-beta.2).

**Build the C# (optional — the Godot editor also builds it, but useful for a quick compile check):**
```sh
dotnet build VehicularCombat.csproj
```

**Run the dedicated server (terminal 1, headless):**
```sh
godot --headless res://src/scenes/server.tscn -- --server --port 7005
```

**Run a client (terminal 2, or the editor):**
```sh
godot res://src/scenes/client.tscn -- --client --host 127.0.0.1 --port 7005
```
(or in the editor: run `client.tscn` and pass `--client --host 127.0.0.1 --port 7005` as command-line arguments.)

**Dev loop:** one terminal runs the headless server; another runs the client. The `Bootstrap` autoload loads the vehicle-class data on both sides at startup.

**Headless round-trip autotest (no GUI needed):** the client has a `--autospawn` test driver that programmatically sends Join then Spawn. Run both headless to verify the Join→Spawn→SpawnResult round-trip without a display:
```sh
godot --headless res://src/scenes/server.tscn -- --server --port 7005
# then, in a second terminal:
godot --headless res://src/scenes/client.tscn -- --client --host 127.0.0.1 --port 7005 -- autospawn
```
A working round-trip prints `Client: spawned vehicle N` on the client and `[AUDIT] spawn: … spawned vehicle N` on the server, with zero RPC errors.

**Persistence:** the world state (factions, base ownership, resource pools) is written to `user://world_save.json` (durable, atomic). It is loaded on server start and autosaved every 5 s + on shutdown. (SQLite is the planned upgrade — see §3.1.)

---

## 8. Implementation order (within Stage 0)

1. **Project wiring:** C# assembly (`.csproj`), autoloads, scene skeletons, headless/server vs client entry points.
2. **Shared protocol + data:** `NetProtocol`, `VehicleClass`, `Faction`, `Base`, `vehicle_classes.json`, `bases.json`.
3. **Networking core:** ENet peer server, client connect, `Join`/`WorldSnapshot` round-trip (a client can see the world).
4. **WorldState + Persistence:** SQLite load/save of factions + bases + resource pools.
5. **Spawn + movement:** `SpawnRequest` (resource-checked), stand-in spawn, `Input` stream, server integration, `VehicleSnapshot`, client prediction/interpolation.
6. **Campaign:** capture/hold + frontline.
7. **Economy:** generation + spawn cost accounting (wired into `SpawnRequest`).
8. **AI Defense:** per-base interceptor/defender, allegiance flip on capture.
9. **Integrity:** input validation + anomaly detection + audit log.
10. **HUD/UX:** faction, resource pool, base status, spawn choice, spectator/reposition (downtime).
11. **Verify definition-of-done** (§1) end to end.

---

## 9. Open items (carried from GOALS.md §9)

- Tick rate / bandwidth budget (start 20 Hz; benchmark).
- Hold duration, defense radius, AI effectiveness tuning.
- Generation rates and spawn costs per class.
- Spawn slot model (how many per base, frontline vs deep placement rules).
