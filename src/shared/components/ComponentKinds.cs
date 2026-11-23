using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// The kinds of capability a vehicle component can provide. Each plane (or other
/// vehicle) scene is built by dropping one or more component nodes under an
/// <c>AirplaneRig</c> (or future VehicleRig) root. The rig aggregates the
/// components' stats into the plane's overall capabilities.
///
/// A component is ALWAYS a node you can drop into the scene and position on the
/// mesh (e.g. put the engine at the wing root, the gun in the nose). Its
/// <see cref="VehicularCombat.Shared.Components"/> stats are [Export]ed so you
/// tune them per-instance in the editor. The SAME node type runs on the client
/// (visual + behavior) and the server (authoritative stat contribution) — the
/// server is headless, so the visual parts are simply not instantiated there.
/// </summary>
public enum ComponentKind
{
    Unknown = 0,

    /// <summary>Aerodynamic surface (wing, tailplane, fuselage section). Adds lift/drag.</summary>
    Airfoil,

    /// <summary>Powerplant (jet engine, piston, turboprop). Adds thrust + fuel burn.</summary>
    Engine,

    /// <summary>Rotor system (main rotor, tail rotor) for helicopters.</summary>
    Rotor,

    /// <summary>Landing gear (wheels + struts). Governs takeoff/landing + ground roll.</summary>
    LandingGear,

    /// <summary>Cockpit / pilot. Governs who controls the plane + the camera anchor.</summary>
    Pilot,

    /// <summary>Sensor suite (radar, gunsight, FLIR). Governs detection + targeting.</summary>
    Radar,

    /// <summary>Weapon (gun, cannon, missile rail). Governs what it can fire + how.</summary>
    Gun,

    /// <summary>Missile / guided-weapon launcher.</summary>
    Missile,

    /// <summary>Empty hardpoint / weapon attachment point (a mount for external stores).</summary>
    Hardpoint,

    /// <summary>Armor / structural plating. Governs how much damage the plane absorbs.</summary>
    Armor,

    /// <summary>General attachment point (a named anchor the user can parent things to).</summary>
    Anchor,
}
