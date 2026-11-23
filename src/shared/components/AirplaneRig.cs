using Godot;
using System.Collections.Generic;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// The ROOT node of an AIRCRAFT scene. This is what you build each plane type as:
///
///   AirplaneRig  (this node = the scene root)
///   ├─ Model          (Node3D)  ← your imported mesh (a .glb/.blend, or a
///   │                             MeshInstance3D). Optional wrapper; if present,
///   │                             it's the visual anchor.
///   ├─ Fuselage       (Airfoil) ← position on the model, set Area/CdZero...
///   ├─ Wing-L         (Airfoil)
///   ├─ Wing-R         (Airfoil)
///   ├─ Tailplane      (Airfoil)
///   ├─ Engine         (JetEngine)
///   ├─ Cockpit        (Pilot)   ← camera anchor lives here
///   ├─ NoseGear       (LandingGear)
///   ├─ MainGear-L     (LandingGear)
///   ├─ MainGear-R     (LandingGear)
///   ├─ Radar          (Radar)
///   ├─ NoseGun        (Gun)
///   ├─ Wing-Missile-L (Missile)
///   └─ Hardpoint-1    (Hardpoint)
///
/// Everything you drop under it is a <see cref="VehicleComponent"/> (or a plain
/// visual node under "Model"). The rig collects all components on ready and
/// exposes the aggregated <see cref="VehicleRig.Aggregate"/> — the single answer
/// to "what can this plane do".
///
/// WHY subclass VehicleRig at all? It gives aircraft a named root in the scene
/// tree + a home for aircraft-specific conveniences (e.g. a quick "is airborne"
/// helper) WITHOUT changing the generic rig. Tanks/ships can use their own
/// VehicleRig subclasses later, all sharing the same component system.
/// </summary>
public partial class AirplaneRig : VehicleRig
{
    /// <summary>
    /// True once the flight model determines this plane is airborne (not on the
    /// ground / gear). Set by the flight model; components (e.g. gear, flaps)
    /// read it. Stage 0: defaults false.
    /// </summary>
    public bool Airborne { get; internal set; }

    /// <summary>
    /// The cockpit component (first Pilot found), or null. The camera / controls
    /// attach to <c>Cockpit.CameraAnchor</c>.
    /// </summary>
    public Pilot? Cockpit => GetComponent<Pilot>();

    /// <summary>All guns on this plane (for the combat system).</summary>
    public List<Gun> Guns => GetComponents<Gun>();

    /// <summary>All missiles on this plane.</summary>
    public List<Missile> Missiles => GetComponents<Missile>();

    /// <summary>All landing gear assemblies on this plane.</summary>
    public List<LandingGear> LandingGears => GetComponents<LandingGear>();

    /// <summary>All engines on this plane (for the flight model / throttle).</summary>
    public List<JetEngine> Engines => GetComponents<JetEngine>();

    // --- Typed lookups (instance methods so they're always in scope). --------
    /// <summary>First component of type T, or null.</summary>
    public T GetComponent<T>() where T : VehicleComponent
    {
        foreach (var c in Components)
            if (c is T t) return t;
        return null;
    }

    /// <summary>All components of type T.</summary>
    public List<T> GetComponents<T>() where T : VehicleComponent
    {
        var list = new List<T>();
        foreach (var c in Components)
            if (c is T t) list.Add(t);
        return list;
    }
}
