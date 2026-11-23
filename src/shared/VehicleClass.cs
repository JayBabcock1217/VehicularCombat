using Godot;
using System.Collections.Generic;

namespace VehicularCombat.Shared;

/// <summary>
/// A vehicle class (air / ground / naval / heli). Stage 0 uses one generic
/// stand-in per class with class-appropriate minimal motion. The class identity
/// and its tuning (max speed, turn rate, spawn cost) are DATA, so adding a 5th
/// class is a JSON entry + optional motion override — no refactor of the
/// spawner or the network protocol (GOALS.md §7.1 "no rework").
/// </summary>
public enum VehicleClassId
{
    Unknown = 0,
    Air = 1,
    Ground = 2,
    Naval = 3,
    Heli = 4,
}

/// <summary>
/// Data definition for a vehicle class, loaded from <c>res://data/vehicle_classes.json</c>.
/// Serializable so it round-trips cleanly with JSON.
/// </summary>
public partial class VehicleClassDef : Resource
{
    [Export] public VehicleClassId Id { get; set; }
    [Export] public string Name { get; set; } = "";

    // --- Minimal stand-in motion tuning (Stage 0) ---------------------------
    /// <summary>Max forward speed, units/sec. Used for input validation too.</summary>
    [Export] public float MaxSpeed { get; set; } = 30f;
    /// <summary>Turn rate, radians/sec.</summary>
    [Export] public float TurnRate { get; set; } = 1.0f;
    /// <summary>
    /// Motion domain for the stand-in. Drives how WASD is mapped:
    /// Ground/Naval = planar (W forward, A/D strafe-ish turn); Air/Heli = 3D pitch.
    /// </summary>
    [Export] public MotionDomain Domain { get; set; } = MotionDomain.Planar;

    // --- Economy (Stage 0) --------------------------------------------------
    /// <summary>Cost in the faction's shared resource pool to spawn this class.</summary>
    [Export] public float SpawnCost { get; set; } = 100f;

    // --- Integrity (Stage 0) ------------------------------------------------
    /// <summary>Max plausible speed for anomaly detection (implausibility margin over MaxSpeed).</summary>
    [Export] public float MaxPlausibleSpeed { get; set; } = 60f;

    public enum MotionDomain { Planar, Aerial }
}

/// <summary>
/// Registry of all vehicle classes. Loaded once from JSON at startup by both
/// client and server so both sides share identical definitions.
/// </summary>
public static class VehicleClassRegistry
{
    private static Dictionary<VehicleClassId, VehicleClassDef> _byId = new();
    private static bool _loaded = false;

    public static IReadOnlyDictionary<VehicleClassId, VehicleClassDef> All => _byId;

    /// <summary>The first loaded class (deterministic: lowest id). Used by the headless autotest.</summary>
    public static (VehicleClassId id, VehicleClassDef def)? FirstClass()
    {
        if (!_loaded) return null;
        (VehicleClassId id, VehicleClassDef def)? best = null;
        foreach (var kv in _byId)
        {
            if (best == null || (int)kv.Key < (int)best.Value.id) best = (kv.Key, kv.Value);
        }
        return best;
    }

    public static VehicleClassDef? Get(VehicleClassId id) =>
        _loaded && _byId.TryGetValue(id, out var def) ? def : null;

    public static bool TryGet(VehicleClassId id, out VehicleClassDef def)
    {
        def = null;
        return _loaded && _byId.TryGetValue(id, out def);
    }

    public static void Load(string path = "res://data/vehicle_classes.json")
    {
        _byId.Clear();
        if (!FileAccess.FileExists(path))
        {
            GD.PushError($"VehicleClassRegistry: data file not found: {path}");
            _loaded = true;
            return;
        }
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushError($"VehicleClassRegistry: could not open {path}");
            _loaded = true;
            return;
        }
        string json = file.GetAsText();
        file.Close();

        var root = JsonUtil.StringToDict(json);
        if (root == null)
        {
            GD.PushError($"VehicleClassRegistry: {path} did not parse to a dictionary");
            _loaded = true;
            return;
        }
        if (!root.ContainsKey("classes"))
        {
            GD.PushError($"VehicleClassRegistry: {path} missing 'classes' key");
            _loaded = true;
            return;
        }
        var classes = (Godot.Collections.Dictionary)root["classes"];
        foreach (var key in classes.Keys)
        {
            var entry = (Godot.Collections.Dictionary)classes[key];
            var def = new VehicleClassDef();
            def.Id = (VehicleClassId)(int)entry["id"];
            def.Name = (string)entry["name"];
            def.MaxSpeed = (float)entry["max_speed"];
            def.TurnRate = (float)entry["turn_rate"];
            def.Domain = (VehicleClassDef.MotionDomain)(int)entry["domain"];
            def.SpawnCost = (float)entry["spawn_cost"];
            def.MaxPlausibleSpeed = (float)entry["max_plausible_speed"];
            _byId[def.Id] = def;
        }
        _loaded = true;
        GD.Print($"VehicleClassRegistry: loaded {_byId.Count} vehicle classes from {path}");
    }

    public static void Reset() { _byId.Clear(); _loaded = false; }
}
