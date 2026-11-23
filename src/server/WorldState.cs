using Godot;
using Godot.Collections;
using System;
using System.Text.Json.Nodes;
using VehicularCombat.Shared;
using Dict = System.Collections.Generic.Dictionary<string, VehicularCombat.Shared.BaseDef>;

namespace VehicularCombat.Server;

/// <summary>
/// The authoritative source of truth for the persistent world: the two factions,
/// all bases (ownership + capture state), and each faction's shared resource pool.
/// Every other server system (Campaign, Economy, AiDefense, Integrity) reads and
/// writes through this object. It is what gets persisted to SQLite across server
/// restarts (GOALS.md §5, §7.1).
/// </summary>
public partial class WorldState : Node
{
    public FactionDef[] Factions { get; private set; } = System.Array.Empty<FactionDef>();
    public Dict Bases { get; private set; } = new();

    /// <summary>Fired after a base changes owner (capture).</summary>
    public event Action<BaseDef>? BaseCaptured;

    /// <summary>Fired whenever any faction's resource pool changes (for HUD/audit).</summary>
    public event Action<int, float>? FactionPoolChanged;

    public override void _Ready()
    {
        VehicleClassRegistry.Load(); // ensure class data is available to both sides
        BuildDefaultWorld();
    }

    /// <summary>
    /// Build the default world: two factions + bases from <c>res://data/bases.json</c>.
    /// Used when there is no saved state to load.
    /// </summary>
    public void BuildDefaultWorld()
    {
        Factions = new FactionDef[Shared.Factions.Count]
        {
            Shared.Factions.CreateDefault(Shared.Factions.Alpha),
            Shared.Factions.CreateDefault(Shared.Factions.Bravo),
        };
        Bases.Clear();
        LoadBasesFromFile("res://data/bases.json");
        GD.Print($"WorldState: built default world with {Factions.Length} factions, {Bases.Count} bases");
    }

    private void LoadBasesFromFile(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            GD.PushError($"WorldState: bases file not found: {path}");
            return;
        }
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) return;
        string json = file.GetAsText();
        file.Close();

        var root = JsonUtil.StringToDict(json);
        if (root == null)
        {
            GD.PushError($"WorldState: {path} did not parse to a dictionary");
            return;
        }
        if (!root.ContainsKey("bases"))
        {
            GD.PushError($"WorldState: {path} missing 'bases' key");
            return;
        }
        var arr = (Godot.Collections.Array)root["bases"];
        for (int i = 0; i < arr.Count; i++)
        {
            var entry = (Dictionary)arr[i];
            var b = new BaseDef();
            b.Id = (string)entry["id"];
            b.Name = (string)entry["name"];
            var p = (float[])entry["position"];
            b.Position = new Vector3(p[0], p[1], p[2]);
            b.Type = (InfrastructureType)(int)entry["type"];
            b.HoldRadius = (float)entry["hold_radius"];
            b.HoldDuration = (float)entry["hold_duration"];
            b.DefenseRadius = (float)entry["defense_radius"];
            b.GenerationRate = (float)entry["generation_rate"];
            if (entry.ContainsKey("owner_faction"))
                b.OwnerFaction = (int)entry["owner_faction"];
            else
                b.OwnerFaction = -1;
            Bases[b.Id] = b;
        }
    }

    // --- Queries ------------------------------------------------------------

    public FactionDef? GetFaction(int id) =>
        id >= 0 && id < Factions.Length ? Factions[id] : null;

    public BaseDef? GetBase(string id) =>
        Bases.TryGetValue(id, out var b) ? b : null;

    /// <summary>Total resource generation rate for a faction (base + captured infrastructure).</summary>
    public float GenerationRateFor(int factionId)
    {
        var f = GetFaction(factionId);
        if (f == null) return 0f;
        float rate = f.BaseGenerationRate;
        foreach (var b in Bases.Values)
        {
            if (b.OwnerFaction == factionId) rate += b.GenerationRate;
        }
        return rate;
    }

    // --- Mutation (authoritative) ------------------------------------------

    /// <summary>Set a base's owner and notify listeners. Clamps capture progress.</summary>
    public void SetBaseOwner(string baseId, int factionId)
    {
        if (!Bases.TryGetValue(baseId, out var b)) return;
        b.OwnerFaction = factionId;
        b.CaptureProgress = 0f;
        b.CapturingFaction = -1;
        BaseCaptured?.Invoke(b);
        GD.Print($"WorldState: base '{baseId}' now owned by faction {factionId}");
    }

    /// <summary>Update a base's capture progress for the capturing faction.</summary>
    public void SetBaseCapture(string baseId, int capturingFaction, float progress)
    {
        if (!Bases.TryGetValue(baseId, out var b)) return;
        b.CapturingFaction = capturingFaction;
        b.CaptureProgress = Mathf.Clamp(progress, 0f, 1f);
    }

    /// <summary>Add to a faction's resource pool (generation tick).</summary>
    public void AddToPool(int factionId, float amount)
    {
        var f = GetFaction(factionId);
        if (f == null) return;
        f.ResourcePool += amount;
        FactionPoolChanged?.Invoke(factionId, f.ResourcePool);
    }

    /// <summary>
    /// Try to spend from a faction's pool. Returns true and deducts on success;
    /// false (no deduction) if insufficient. This is the gate for spawning.
    /// </summary>
    public bool TrySpend(int factionId, float amount)
    {
        var f = GetFaction(factionId);
        if (f == null || f.ResourcePool < amount) return false;
        f.ResourcePool -= amount;
        FactionPoolChanged?.Invoke(factionId, f.ResourcePool);
        return true;
    }

    // --- Persistence (de)serialization -------------------------------------
    // The shape here is exactly what Persistence.cs writes to / reads from SQLite.

    /// <summary>Serialize the whole world state to a JSON string (for persistence).</summary>
    public string ToJson()
    {
        var root = new JsonObject();
        var factions = new JsonArray();
        foreach (var f in Factions)
        {
            factions.Add(new JsonObject
            {
                ["id"] = f.Id,
                ["name"] = f.Name,
                ["color"] = new JsonArray { f.Color.R, f.Color.G, f.Color.B, f.Color.A },
                ["base_generation_rate"] = f.BaseGenerationRate,
                ["resource_pool"] = f.ResourcePool,
            });
        }
        root["factions"] = factions;

        var bases = new JsonArray();
        foreach (var b in Bases.Values)
        {
            bases.Add(new JsonObject
            {
                ["id"] = b.Id,
                ["name"] = b.Name,
                ["position"] = new JsonArray { b.Position.X, b.Position.Y, b.Position.Z },
                ["type"] = (int)b.Type,
                ["hold_radius"] = b.HoldRadius,
                ["hold_duration"] = b.HoldDuration,
                ["defense_radius"] = b.DefenseRadius,
                ["generation_rate"] = b.GenerationRate,
                ["owner_faction"] = b.OwnerFaction,
                ["capture_progress"] = b.CaptureProgress,
                ["capturing_faction"] = b.CapturingFaction,
            });
        }
        root["bases"] = bases;

        return root.ToJsonString();
    }

    /// <summary>Load world state from a JSON string (the DB blob). Returns false on parse failure.</summary>
    public bool LoadFromJson(string json)
    {
        var root = JsonUtil.StringToDict(json);
        if (root == null) return false;
        if (!root.ContainsKey("factions") || !root.ContainsKey("bases")) return false;

        var factionsArr = (Godot.Collections.Array)root["factions"];
        Factions = new FactionDef[factionsArr.Count];
        for (int i = 0; i < factionsArr.Count; i++)
        {
            var e = (Dictionary)factionsArr[i];
            var f = new FactionDef
            {
                Id = (int)e["id"],
                Name = (string)e["name"],
                BaseGenerationRate = (float)e["base_generation_rate"],
                ResourcePool = (float)e["resource_pool"],
            };
            var c = (float[])e["color"];
            f.Color = new Color(c[0], c[1], c[2], c[3]);
            Factions[f.Id] = f;
        }

        Bases.Clear();
        var basesArr = (Godot.Collections.Array)root["bases"];
        for (int i = 0; i < basesArr.Count; i++)
        {
            var e = (Dictionary)basesArr[i];
            var b = new BaseDef
            {
                Id = (string)e["id"],
                Name = (string)e["name"],
                Type = (InfrastructureType)(int)e["type"],
                HoldRadius = (float)e["hold_radius"],
                HoldDuration = (float)e["hold_duration"],
                DefenseRadius = (float)e["defense_radius"],
                GenerationRate = (float)e["generation_rate"],
                OwnerFaction = (int)e["owner_faction"],
                CaptureProgress = (float)e["capture_progress"],
                CapturingFaction = (int)e["capturing_faction"],
            };
            var p = (float[])e["position"];
            b.Position = new Vector3(p[0], p[1], p[2]);
            Bases[b.Id] = b;
        }
        GD.Print($"WorldState: loaded persisted state ({Factions.Length} factions, {Bases.Count} bases)");
        return true;
    }
}
