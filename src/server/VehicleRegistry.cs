using Godot;
using Godot.Collections;
using System.Collections.Generic;
using VehicularCombat.Shared;

namespace VehicularCombat.Server;

/// <summary>
/// Tracks all live vehicle stand-ins on the server. Assigns ids, owns the nodes,
/// and provides the iteration the Campaign (hold), AiDefense (intercept), and
/// snapshot broadcasting need.
/// </summary>
public partial class VehicleRegistry : Node
{
    private readonly System.Collections.Generic.Dictionary<int, VehicleStandin> _byId = new();
    private int _nextId = 1;

    /// <summary>Allocate a new unique vehicle id.</summary>
    public int NextId() => _nextId++;

    /// <summary>Register a stand-in node under an id.</summary>
    public void Add(int id, VehicleStandin v) => _byId[id] = v;

    public VehicleStandin? Get(int id) => _byId.TryGetValue(id, out var v) ? v : null;

    public void Remove(int id) => _byId.Remove(id);

    public int Count => _byId.Count;

    /// <summary>Iterate all live vehicles (skips destroyed).</summary>
    public IEnumerable<VehicleStandin> Alive()
    {
        foreach (var v in _byId.Values)
        {
            if (v.Alive) yield return v;
        }
    }

    /// <summary>Build the per-vehicle snapshot array for broadcasting.</summary>
    public Godot.Collections.Array BuildSnapshotArray()
    {
        var arr = new Godot.Collections.Array();
        foreach (var v in _byId.Values)
        {
            if (!v.Alive) continue;
            arr.Add(NetProtocol.VehicleEntry(
                v.Id, v.Class, v.Faction,
                v.Motion.Position, v.Motion.Rotation, v.Motion.Velocity, v.Alive));
        }
        return arr;
    }

    /// <summary>Clear all vehicles (e.g. on world reset).</summary>
    public void Clear()
    {
        foreach (var v in _byId.Values) v.QueueFree();
        _byId.Clear();
    }
}
