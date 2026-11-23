using Godot;

namespace VehicularCombat.Shared;

/// <summary>
/// One spawnable vehicle, as a Godot <see cref="Resource"/> you create and edit
/// in the editor's inspector.
///
/// HOW TO ADD A VEHICLE (all in the editor, no code, no running the game):
///   1. In the FileSystem dock, right-click the folder for your faction → type
///      (e.g. data/vehicles/0/air) → "New Resource…" → pick
///      <c>VehicleEntry</c> → save it there (e.g. f5.tres).
///   2. Select the new resource and fill in the inspector:
///        Name  – the player-facing label
///        Class – Air / Ground / Naval / Heli (drives motion + server stand-in)
///        Scene – drag your .tscn in (what the client renders for it)
///   3. Save the resource (Ctrl+S). The client picks it up from the folder.
///
/// FOLDER LAYOUT = the catalog (data/vehicles/):
///   data/vehicles/&lt;factionId&gt;/&lt;typeKey&gt;/*.tres
///   e.g.  data/vehicles/0/air/f5.tres
///         data/vehicles/1/ground/m1.tres
/// Each subfolder of data/vehicles/ is a FACTION (its name is the faction id).
/// Each subfolder of a faction is a TYPE (air/ground/naval/heli or custom).
/// Each .tres inside a type folder is a VEHICLE. This is the "area in the editor
/// to configure all this" — you add/remove factions/types/vehicles as folders
/// and resource files, and edit each vehicle's fields in the inspector.
/// </summary>
[GlobalClass]
public partial class VehicleEntry : Resource
{
    /// <summary>Player-facing display name.</summary>
    [Export] public string Name { get; set; } = "New Vehicle";

    /// <summary>
    /// The vehicle class (motion/stats/domain/spawn-cost) — drives the server
    /// stand-in. Air/Tank/Ship/Heli.
    /// </summary>
    [Export] public VehicleClassId Class { get; set; } = VehicleClassId.Air;

    /// <summary>
    /// The CLIENT scene that renders this vehicle (drag a .tscn in). The server
    /// stays headless and ignores this. Empty = procedural stand-in.
    /// </summary>
    [Export] public NodePath Scene { get; set; } = default;

    public VehicleType ToType() => new()
    {
        Name = Name,
        ClassId = (int)Class,
        Scene = Scene.IsEmpty ? "" : Scene.ToString(),
    };

    public static VehicleEntry FromType(VehicleType t) => new()
    {
        Name = t.Name,
        Class = t.Class,
        Scene = string.IsNullOrEmpty(t.Scene) ? default : new NodePath(t.Scene),
    };
}
