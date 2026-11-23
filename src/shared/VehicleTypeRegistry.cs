using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VehicularCombat.Shared;

/// <summary>
/// One spawnable vehicle (a specific model/scene the player can pick). A
/// <see cref="VehicleType"/> (e.g. "F-5", "MiG-29") is a list of these. The
/// <see cref="ClassId"/> ties it to <see cref="VehicleClassDef"/> (motion/stats/
/// domain); the <see cref="Scene"/> is what the CLIENT renders for it.
/// </summary>
public class VehicleType
{
    public string Name { get; set; } = "";
    public int ClassId { get; set; } = (int)VehicleClassId.Unknown;
    public string Scene { get; set; } = "";

    public VehicleClassId Class => (VehicleClassId)ClassId;
    /// <summary>The motion domain (air/ground/naval/heli), derived from the class.</summary>
    public VehicleClassDef.MotionDomain Domain
    {
        get
        {
            var def = VehicleClassRegistry.Get(Class);
            return def?.Domain ?? VehicleClassDef.MotionDomain.Planar;
        }
    }
}

/// <summary>
/// The data-driven vehicle spawn catalog. Loaded once from
/// <c>res://data/vehicles.json</c> by both client and server.
///
/// Structure (mirrors the JSON):
///   factions -> { factionId -> { types -> { typeId -> VehicleEntry[] } } }
///
/// This is what makes "the player picks a vehicle TYPE, then a specific VEHICLE
/// of that type for their faction" true, and what the editor panel edits.
/// Adding a faction / type / vehicle is a JSON change (or an editor-panel change
/// that re-saves the JSON) — no code rework (GOALS.md §7.1).
///
/// The <see cref="VehicleSceneRegistry"/> still maps CLASS -> scene for the
/// procedural/stand-in fallback; this catalog maps the specific chosen vehicle's
/// SCENE, which the client uses to render its own vehicle.
/// </summary>
public static class VehicleTypeRegistry
{
    /// <summary>
    /// The editable catalog root. Layout: data/vehicles/&lt;factionId&gt;/&lt;typeKey&gt;/*.tres,
    /// where each .tres is a <see cref="VehicleEntry"/> (edited in the editor's
    /// inspector). Add/remove factions/types/vehicles as folders + resource files.
    /// </summary>
    public const string DefaultRoot = "res://data/vehicles";
    /// <summary>Legacy JSON catalog (fallback only, for migration / headless tests).</summary>
    public const string DefaultJsonPath = "res://data/vehicles.json";

    // factions -> types -> vehicles
    private static readonly Dictionary<int, Dictionary<string, List<VehicleType>>> _catalog = new();
    private static bool _loaded;

    /// <summary>All faction ids present in the catalog (sorted).</summary>
    public static IReadOnlyList<int> FactionIds
    {
        get
        {
            var ids = new List<int>(_catalog.Keys);
            ids.Sort();
            return ids;
        }
    }

    /// <summary>All type keys for a faction (e.g. "air", "ground"), sorted.</summary>
    public static IReadOnlyList<string> TypesForFaction(int factionId)
    {
        if (!_catalog.TryGetValue(factionId, out var types)) return new List<string>();
        var keys = new List<string>(types.Keys);
        keys.Sort(StringComparer.Ordinal);
        return keys;
    }

    /// <summary>
    /// Every spawnable vehicle for a faction, tagged with its type key. This is
    /// what the connect UI uses to populate the vehicle dropdown for a domain:
    /// filter by <paramref name="typeKey"/> (or pass null for all).
    /// </summary>
    public static List<(string TypeKey, VehicleType Vehicle)> VehiclesForFaction(int factionId, string typeKey = null)
    {
        var result = new List<(string, VehicleType)>();
        if (!_catalog.TryGetValue(factionId, out var types)) return result;
        foreach (var kv in types)
        {
            if (typeKey != null && kv.Key != typeKey) continue;
            foreach (var v in kv.Value) result.Add((kv.Key, v));
        }
        return result;
    }

    /// <summary>Direct access for the editor (mutable in-memory until saved).</summary>
    public static Dictionary<string, List<VehicleType>> MutableTypesFor(int factionId)
    {
        if (!_catalog.ContainsKey(factionId)) _catalog[factionId] = new Dictionary<string, List<VehicleType>>();
        return _catalog[factionId];
    }

    /// <summary>Remove a faction and all its types/vehicles (editor).</summary>
    public static void RemoveFaction(int factionId) => _catalog.Remove(factionId);

    /// <summary>True if the faction exists in the catalog.</summary>
    public static bool HasFaction(int factionId) => _catalog.ContainsKey(factionId);

    // ------------------------------------------------------------------
    // Load / Save
    // ------------------------------------------------------------------

    /// <summary>
    /// Load the catalog from the editable folder structure
    /// (<c>data/vehicles/&lt;faction&gt;/&lt;type&gt;/*.tres</c> — each .tres is a
    /// <see cref="VehicleEntry"/> edited in the editor's inspector). If the folder
    /// has no vehicles, falls back to the legacy JSON so old data still works.
    /// </summary>
    public static void Load(string root = DefaultRoot, string jsonPath = DefaultJsonPath)
    {
        if (LoadFromFolder(root)) return;
        LoadFromJson(jsonPath);
    }

    /// <summary>
    /// Scan <c>data/vehicles/&lt;factionId&gt;/&lt;typeKey&gt;/*.tres</c> into the catalog.
    /// Each subfolder of <paramref name="root"/> is a faction (name = faction id);
    /// each subfolder of a faction is a type; each .tres in a type folder is a
    /// vehicle. Returns false if no vehicles were found.
    /// </summary>
    private static bool LoadFromFolder(string root)
    {
        _catalog.Clear();
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(root)))
        {
            GD.Print($"VehicleTypeRegistry: no {root}/ folder; will use JSON fallback.");
            return false;
        }
        int total = 0;
        var dir = DirAccess.Open(root);
        if (dir == null)
        {
            GD.PushError($"VehicleTypeRegistry: could not open {root}");
            return false;
        }
        dir.ListDirBegin();
        string factionEntry = dir.GetNext();
        while (factionEntry != "")
        {
            if (!dir.CurrentIsDir() || factionEntry.StartsWith("."))
            {
                factionEntry = dir.GetNext();
                continue;
            }
            // A faction folder: its name is the faction id.
            if (!int.TryParse(factionEntry, out int fid))
            {
                GD.PushWarning($"VehicleTypeRegistry: faction folder '{factionEntry}' is not an int; skipped");
                factionEntry = dir.GetNext();
                continue;
            }
            string factionPath = root.PathJoin(factionEntry);
            var typesDict = new Dictionary<string, List<VehicleType>>();
            var fdir = DirAccess.Open(factionPath);
            if (fdir != null)
            {
                fdir.ListDirBegin();
                string typeEntry = fdir.GetNext();
                while (typeEntry != "")
                {
                    if (fdir.CurrentIsDir() && !typeEntry.StartsWith("."))
                    {
                        string typePath = factionPath.PathJoin(typeEntry);
                        var list = new List<VehicleType>();
                        var tdir = DirAccess.Open(typePath);
                        if (tdir != null)
                        {
                            tdir.ListDirBegin();
                            string vEntry = tdir.GetNext();
                            while (vEntry != "")
                            {
                                if (!tdir.CurrentIsDir() && vEntry.EndsWith(".tres"))
                                {
                                    string vPath = typePath.PathJoin(vEntry);
                                    var e = GD.Load<VehicleEntry>(vPath);
                                    if (e != null)
                                    {
                                        list.Add(e.ToType());
                                        total++;
                                    }
                                    else
                                    {
                                        GD.PushWarning($"VehicleTypeRegistry: could not load vehicle {vPath}");
                                    }
                                }
                                vEntry = tdir.GetNext();
                            }
                        }
                        typesDict[typeEntry] = list;
                    }
                    typeEntry = fdir.GetNext();
                }
            }
            if (typesDict.Count > 0) _catalog[fid] = typesDict;
            factionEntry = dir.GetNext();
        }
        _loaded = true;
        if (total == 0)
        {
            GD.Print($"VehicleTypeRegistry: {root}/ has no vehicles; will use JSON fallback.");
            _catalog.Clear();
            return false;
        }
        GD.Print($"VehicleTypeRegistry: loaded {total} vehicle(s) across {_catalog.Count} faction(s) from {root}/");
        return true;
    }

    private static void LoadFromJson(string path)
    {
        _catalog.Clear();
        if (!FileAccess.FileExists(path))
        {
            GD.PushError($"VehicleTypeRegistry: data file not found: {path}");
            _loaded = true;
            return;
        }
        string json = FileAccess.Open(path, FileAccess.ModeFlags.Read).GetAsText();
        var root = JsonUtil.StringToDict(json);
        if (root == null)
        {
            GD.PushError($"VehicleTypeRegistry: {path} did not parse to a dictionary");
            _loaded = true;
            return;
        }
        if (!root.ContainsKey("factions"))
        {
            GD.PushError($"VehicleTypeRegistry: {path} missing 'factions' key");
            _loaded = true;
            return;
        }
        var factions = (Godot.Collections.Dictionary)root["factions"];
        foreach (var fidKey in factions.Keys)
        {
            int fid = (int)fidKey;
            var fEntry = (Godot.Collections.Dictionary)factions[fidKey];
            var typesDict = new Dictionary<string, List<VehicleType>>();
            if (fEntry.ContainsKey("types"))
            {
                var types = (Godot.Collections.Dictionary)fEntry["types"];
                foreach (var tKey in types.Keys)
                {
                    var tEntry = (Godot.Collections.Dictionary)types[tKey];
                    var list = new List<VehicleType>();
                    if (tEntry.ContainsKey("vehicles"))
                    {
                        var vehicles = (Godot.Collections.Array)tEntry["vehicles"];
                        foreach (var ve in vehicles)
                        {
                            var e = (Godot.Collections.Dictionary)ve;
                            list.Add(new VehicleType
                            {
                                Name = (string)e["name"],
                                ClassId = (int)e["class_id"],
                                Scene = e.ContainsKey("scene") ? (string)e["scene"] : "",
                            });
                        }
                    }
                    typesDict[(string)tKey] = list;
                }
            }
            _catalog[fid] = typesDict;
        }
        _loaded = true;
        GD.Print($"VehicleTypeRegistry: loaded {_catalog.Count} faction(s) from {path}");
    }

    /// <summary>
    /// Write the current in-memory catalog back to the folder structure
    /// (<c>data/vehicles/&lt;faction&gt;/&lt;type&gt;/&lt;name&gt;.tres</c>). Called by the
    /// in-game editor panel after edits so the files the editor's inspector shows
    /// are up to date.
    /// </summary>
    public static void Save(string root = DefaultRoot)
    {
        int count = 0;
        foreach (var kv in _catalog)
        {
            foreach (var t in kv.Value)
            {
                string typePath = root.PathJoin(kv.Key.ToString()).PathJoin(t.Key);
                var tdir = DirAccess.Open(typePath);
                if (tdir == null)
                {
                    DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(typePath));
                    tdir = DirAccess.Open(typePath);
                }
                if (tdir == null)
                {
                    GD.PushError($"VehicleTypeRegistry: could not create {typePath}");
                    continue;
                }
                foreach (var v in t.Value)
                {
                    string fileName = SanitizeFileName(v.Name) + ".tres";
                    var e = VehicleEntry.FromType(v);
                    var err = ResourceSaver.Save(e, typePath.PathJoin(fileName));
                    if (err == Error.Ok) count++;
                    else GD.PushError($"VehicleTypeRegistry: ResourceSaver failed ({err}) for {typePath}/{fileName}");
                }
            }
        }
        GD.Print($"VehicleTypeRegistry: saved {count} vehicle(s) under {root}/");
    }

    /// <summary>Make a string safe to use as a .tres filename.</summary>
    private static string SanitizeFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "vehicle";
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
        return sb.ToString();
    }

    public static void Reset() { _catalog.Clear(); _loaded = false; }
}
