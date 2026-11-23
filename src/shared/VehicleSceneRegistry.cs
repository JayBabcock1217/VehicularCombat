using Godot;
using System.Collections.Generic;

namespace VehicularCombat.Shared;

/// <summary>
/// Maps a vehicle class to the SCENE that renders it. This is what makes "each
/// plane type is a new scene" true: you build a scene (root = <c>AirplaneRig</c>,
/// with your model + components dropped in), register its path here for a class,
/// and the client instantiates THAT scene for the local vehicle.
///
/// WHY a registry (not a hardcode in the client):
///  * Data-driven: adding a new plane type = build a scene + one line here. No
///    client/spawner/protocol rework (GOALS.md "no rework").
///  * The SAME registry runs on client + server, so both agree on what a class is.
///  * Falls back to the procedural <c>VehicleVisual</c> when a class has no scene
///    registered, so un-modeled classes still render.
///
/// Registration: call <see cref="Register"/> at startup (the client does this in
/// its _Ready). Paths are res:// paths to .tscn files.
/// </summary>
public static class VehicleSceneRegistry
{
    private static readonly Dictionary<VehicleClassId, string> _byClass = new();
    private static bool _loaded;

    /// <summary>All registered class → scene-path mappings.</summary>
    public static IReadOnlyDictionary<VehicleClassId, string> All => _byClass;

    /// <summary>Register (or replace) the scene for a vehicle class.</summary>
    public static void Register(VehicleClassId cls, string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath)) return;
        _byClass[cls] = scenePath;
    }

    /// <summary>
    /// Load the scene resource for a class, or null if none is registered (the
    /// caller then falls back to a procedural stand-in).
    /// </summary>
    public static PackedScene? GetScene(VehicleClassId cls)
    {
        if (!_byClass.TryGetValue(cls, out var path)) return null;
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"VehicleSceneRegistry: scene not found for {cls}: {path}");
            return null;
        }
        return GD.Load<PackedScene>(path);
    }

    /// <summary>True if a real scene is registered for this class.</summary>
    public static bool HasScene(VehicleClassId cls) => _byClass.ContainsKey(cls);

    /// <summary>
    /// Register the default Stage 0 mappings. Call once at client startup. The
    /// paths point at the sample scenes in <c>res://src/vehicles/</c>.
    /// </summary>
    public static void RegisterDefaults()
    {
        Register(VehicleClassId.Air, "res://src/vehicles/Aircraft/airplane.tscn");
        // Ground/Naval/Heli scenes come as they're built; until then they fall
        // back to the procedural VehicleVisual.
        _loaded = true;
    }

    /// <summary>Clear all registrations (for tests).</summary>
    public static void Reset() { _byClass.Clear(); _loaded = false; }
}
