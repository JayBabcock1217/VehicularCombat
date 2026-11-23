using Godot;
using VehicularCombat.Shared;
using VehicularCombat.Shared.Components;

namespace VehicularCombat;

/// <summary>
/// Shared bootstrap autoload. Runs on BOTH the client and the server (and in the
/// editor). Loads the data-driven vehicle-class registry once at startup so all
/// code can rely on it. Mode-specific entry points (Server / Client scenes) do the
/// rest.
/// </summary>
public partial class Bootstrap : Node
{
    public override void _Ready()
    {
        VehicleClassRegistry.Load();
        VehicleTypeRegistry.Load();
        Mode mode = GameConfig.ResolveMode();
        Vehicles.Init(mode);
        GD.Print($"Bootstrap: running as {mode}; {VehicleClassRegistry.All.Count} vehicle classes, {VehicleTypeRegistry.FactionIds.Count} faction(s) in the vehicle catalog.");
    }
}
