# Airplane Component System

How to build a plane type: **each plane is a scene** whose root is an
`AirplaneRig`, into which you drop **your model** + **component nodes** that give
it its functionality. This is the "attach functionality to the mesh" system.

## The two parts

```
AirplaneRig  (scene root — collects everything, exposes "what can this plane do")
├── Model            ← plain Node3D. Instance your exported per-aircraft .glb
│                      into it (drag from the FileSystem dock, or right-click →
│                      Instance). Set its transform manually.
├── <components>     ← one node per capability, positioned on the model
```

1. **The model** — a `Node3D`/`MeshInstance3D` child (optionally wrapped in a node
   named `Model`). This is the visual. It has no functionality on its own.
2. **The components** — drop-in `Node3D`s you position on the mesh. Each carries
   `[Export]`ed stats. The rig sums them into the plane's capabilities.

## The components (drop-in node types)

| Node type     | Kind      | What it gives the plane                          | Key stats |
|---------------|-----------|--------------------------------------------------|-----------|
| `Airfoil`     | 1         | Lift/drag (wing, tailplane, fuselage section)    | Area, AspectRatio, ClMax, CdZero |
| `Engine`      | 2         | Thrust + fuel (jet / piston / turboprop)         | MaxThrust, FuelFlowPerSec, FuelCapacity, SpoolUpSec, Blades |
| `Rotor`       | 3         | Rotor lift (helicopters)                         | MaxThrust, Blades |
| `LandingGear` | 3         | Takeoff/landing + gear retraction                | Wheels, GearSpeedMax, Retractable, RetractSec |
| `Pilot`       | 5         | Cockpit + **camera anchor** + crew               | Crew, ControlAuthority |
| `Radar`       | 6         | Detection + targeting sensor                     | DetectRange, ScanRate, ConeFov |
| `Gun`         | 7         | A gun / cannon                                   | FireRate, Damage, Range, BulletSpeed, Ammo |
| `Missile`     | 8         | A guided-weapon launcher                         | MissileSpeed, MissileRange, MissileDamage, MissileCount |
| `Hardpoint`   | 9         | An empty mount for external stores               | Label |
| `Armor`       | 10        | Damage resistance + structural HP                | ArmorValue, ArmorHealth |
| `Anchor`      | 11        | A named attachment point (camera, VFX, etc.)     | Label |

Each is in `src/shared/components/`. They all derive from `VehicleComponent`, so
they share the same `[Export] Stats` + behavior hooks.

## How the model works

Each aircraft is **one exported mesh** (a `.glb`). You export each aircraft's
collection from Blender as a single `.glb`, and in the aircraft's scene you
**instance that `.glb` into the `Model` node** (a plain `Node3D`). No script, no
fields — just drag the `.glb` in and set its transform. One file = one aircraft.

- Source: `Blender/Aircraft.blend` holds all the aircraft (one collection each).
- Export: in Blender, select a collection's objects → `File → Export → glTF 2.0`
  (`.glb`) → save into the project (e.g. `res://Blender/ExportedModels/…`).
- Each aircraft scene (`f5e.tscn`, `mig21.tscn`, …) is an **instance of the
  `airplane.tscn` template** with its `Model` node populated by its own `.glb`.

## How to build a new plane type

1. **Export the aircraft's mesh** — in Blender, select the aircraft's collection
   objects and export them as one `.glb` into the project. Re-export whenever you
   change the model. Orientation convention: **forward/nose = +X, up = +Y,
   left = +Z** (matches the whole game); if the export uses a different axis
   layout, fix it by rotating the instanced model node (step 3).
2. **Make a scene instance of the template** — in the editor, drag
   `src/vehicles/Aircraft/airplane.tscn` from the FileSystem dock into a new
   scene (or Save As) and save it as `src/vehicles/Aircraft/<your_plane>.tscn`.
   The template is a **blank shell**: an `AirplaneRig` root (a `CharacterBody3D`,
   so it's a proper kinematic body the flight model can drive) + an empty `Model`
   node (a plain `Node3D`) + a `Collider` (`CollisionShape3D` + `BoxShape3D`). It
   has **no components** and **no model yet** — you add both.
3. **Instance your model** — in your new instance, select the `Model` node and
   instance the aircraft's `.glb` into it: drag the `.glb` from the FileSystem
   dock onto the `Model` node, or right-click `Model` → **Instance** → pick the
   `.glb`. Then set the instanced node's **Transform** (scale/position/rotation)
   in the Inspector to line it up with the rig origin, and resize the **Collider**
   to match the model.
4. **Add the components** — right-click a node in the Scene dock → Attach Script,
   and pick a component class (they're still in `src/shared/components/`, e.g.
   `Engine.cs`, `Gun.cs`, `Airfoil.cs`, `LandingGear.cs`, `Pilot.cs`, `Radar.cs`,
   `Missile.cs`, `Hardpoint.cs`, `Armor.cs`, `Anchor.cs`). Position each where it
   physically is (engine at the wing root, gun in the nose, gear under the wings,
   cockpit at the seat). Give each a `label`.
5. **Tune the stats** — select a component, edit its `Stats` in the Inspector.
   (Empty stats get sensible defaults automatically, so a freshly-dropped node
   still works.)
6. **Register it** — add a `VehicleEntry` (`.tres`) in
   `data/vehicles/<faction>/<type>/` whose `Scene` points at your new
   `<your_plane>.tscn`. (The catalog is editor-editable; see `VehicleTypeRegistry`.)
   For the legacy class-keyed fallback you can also map it in
   `VehicleSceneRegistry.RegisterDefaults()` (`src/shared/VehicleSceneRegistry.cs`):
   ```csharp
   Register(VehicleClassId.Air, "res://src/vehicles/<your_plane>.tscn");
   ```
   (Later this becomes data-driven so you don't edit code — for now it's one line.)

That's it. When a player spawns that class, the client instantiates **your scene**
(model + components) and drives its position/rotation from the server snapshot.

## The rig: "what can this plane do"

`AirplaneRig` (in `src/shared/components/AirplaneRig.cs`) collects all components
on ready and exposes an aggregated snapshot (`RigAggregate`) — total thrust, mass,
HP, weapon list with mount positions, radar range, gear, etc. The flight model,
combat, and HUD read `rig.Aggregate` (or the typed helpers `rig.Guns`,
`rig.Missiles`, `rig.Cockpit`, `rig.Engines`, `rig.LandingGears`) instead of
poking individual components.

```csharp
var rig = myPlane as AirplaneRig;
rig.Aggregate.MaxThrust;     // total engine thrust
rig.Aggregate.Health;        // total HP (armor + structural)
rig.Guns;                    // List<Gun> — every gun
rig.Cockpit.CameraAnchor;    // the camera anchor Node3D
```

## Client vs server (why components live in `shared/`)

The same component node classes run on **both** sides:
- **Server (headless):** reads each component's `Stats` for the authoritative sim
  (thrust, HP, weapons). Visuals/behavior are never run (no rendering).
- **Client:** reads the same stats **and** runs each component's behavior/visual
  hooks (spinning prop, muzzle flash, radar sweep, gear retract).

The rig gates the behavior hooks on `Vehicles.IsClient`, so the headless server
never tries to animate. Adding a new component kind needs **no** change to the rig,
spawner, or network protocol — the rig just sums over whatever components are
present (the "no rework" rule).

## What's wired vs. what's next (the "make it perform" phase)

**Wired (this phase):** the drop-in component system, the rig aggregation, the
scene instantiation on spawn, and the client-side *visual* behavior of each
component (props spin, guns flash, radar sweeps, gear fades).

**Not wired yet (next):** each component's actual *job*:
- `Engine` → apply thrust to the flight model (`SetThrottle` is the hook).
- `Airfoil` → lift/drag in the flight model.
- `LandingGear` → takeoff/landing physics + ground contact.
- `Gun`/`Missile` → projectiles + damage (`SetFiring` is the hook).
- `Radar` → detection of other vehicles.
- `Pilot` → hook the controls + camera to `CameraAnchor`.

The hooks (`SetThrottle`, `SetFiring`, `SetDeployed`, `SetSpeed`, `CameraAnchor`)
already exist on the components so the behavior phase just fills them in.
