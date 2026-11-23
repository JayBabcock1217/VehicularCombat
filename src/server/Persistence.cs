using Godot;
using System;
using System.IO;

namespace VehicularCombat.Server;

/// <summary>
/// Durable persistence of the world state, so the campaign (factions, base
/// ownership, resource pools) survives server restarts (GOALS.md §5.1, §7.1).
///
/// Stage 0 uses a **durable JSON file** in Godot's user data dir. This works with
/// zero external dependencies (important: the Godot .NET SDK does not copy NuGet
/// packages into the runtime output, and no SQLite GDExtension is present in this
/// Godot 4.8 build).
///
/// <b>Upgrade path to SQLite:</b> the load/save contract (<see cref="TryLoad"/> /
/// <see cref="Save"/> and <see cref="WorldState.ToJson"/>/
/// <see cref="WorldState.LoadFromJson"/>) is storage-agnostic. When the economy
/// grows (per-faction transaction history, audit queries, concurrent access), swap
/// the file backend for SQLite — the Godot SQLite GDExtension (or a server-side
/// ADO.NET process) — without touching any other system.
/// </summary>
public partial class Persistence : Node
{
    private const string FileName = "world_save.json";
    private string _path;
    private bool _available;

    /// <summary>Initialize the persistence path. Call once before first use.</summary>
    public void Init(WorldState world)
    {
        _path = ProjectSettings.GlobalizePath("user://" + FileName);
        try
        {
            // Ensure the user dir exists (it normally does, but be safe).
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            _available = true;
            GD.Print($"Persistence: JSON file backend at {_path}");
        }
        catch (Exception e)
        {
            _available = false;
            GD.PushError($"Persistence: init failed ({e.Message}) — world state will NOT persist.");
        }
    }

    /// <summary>Save the current WorldState to the durable file. No-op if unavailable.</summary>
    public void Save()
    {
        if (!_available) return;
        // The Server passes the WorldState via the save callback; see Server.cs.
    }

    /// <summary>Save a given WorldState's JSON to the file atomically (write temp, rename).</summary>
    public void Save(string json)
    {
        if (!_available) return;
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true); // atomic-ish on the same volume
        }
        catch (Exception e)
        {
            GD.PushError($"Persistence: save failed: {e.Message}");
        }
    }

    /// <summary>
    /// Try to load the WorldState from the durable file. Returns true on success;
    /// the caller should fall back to <c>WorldState.BuildDefaultWorld()</c> on false.
    /// </summary>
    public bool TryLoad(WorldState world)
    {
        if (!_available) return false;
        try
        {
            if (!File.Exists(_path)) return false;
            string json = File.ReadAllText(_path);
            return !string.IsNullOrEmpty(json) && world.LoadFromJson(json);
        }
        catch (Exception e)
        {
            GD.PushError($"Persistence: load failed: {e.Message}");
            return false;
        }
    }

    public bool Available => _available;
}
