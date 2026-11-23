using Godot;
using Godot.Collections;
using System.Collections.Generic;
using VehicularCombat.Shared;

namespace VehicularCombat.Server;

/// <summary>
/// AI spawn-defense (GOALS.md §5.4). Each base has a defense that, while the base
/// is faction-owned, intercepts hostile vehicles entering its defense radius.
/// When the base is captured, the defense allegiance flips automatically.
///
/// Stage 0 implementation: a simple "interceptor" per base. Each tick it finds
/// the nearest hostile (to the base owner) inside the defense radius and applies
/// placeholder damage; enough damage destroys the vehicle. The *structure*
/// (per-base, allegiance-flipping, intercept-on-entry) is fixed; the *tuning*
/// (range, effectiveness, scaling) is an open item (GOALS.md §9).
/// </summary>
public partial class AiDefense : Node
{
    [Export] private WorldState _world;
    [Export] private VehicleRegistry _vehicles;

    /// <summary>Damage per second applied by the base's AI to a hostile in range.</summary>
    [Export] public float DamagePerSecond { get; set; } = 25f;
    /// <summary>Health a vehicle stand-in has before AI destruction (placeholder).</summary>
    [Export] public float StandinHealth { get; set; } = 100f;

    private readonly System.Collections.Generic.Dictionary<int, float> _hp = new(); // vehicleId -> current hp

    public void Setup(WorldState world, VehicleRegistry vehicles)
    {
        _world = world;
        _vehicles = vehicles;
        _world.BaseCaptured += _ => { /* allegiance flip is implicit: defense reads owner each tick */ };
    }

    /// <summary>
    /// Advance AI defense for all bases by one tick. <paramref name="dt"/> seconds.
    /// Returns the list of vehicle ids destroyed this tick (for snapshot/audit).
    /// </summary>
    public List<int> Tick(float dt)
    {
        var destroyed = new List<int>();
        if (_world == null || _vehicles == null) return destroyed;

        foreach (var b in _world.Bases.Values)
        {
            if (b.OwnerFaction < 0) continue; // neutral base: no defense
            int owner = b.OwnerFaction;

            // Find the nearest hostile vehicle inside the defense radius.
            VehicleStandin? target = null;
            float bestDist = float.MaxValue;
            foreach (var v in _vehicles.Alive())
            {
                if (v.Faction == owner) continue; // friendly
                float d = v.Motion.Position.DistanceTo(b.Position);
                if (d <= b.DefenseRadius && d < bestDist)
                {
                    bestDist = d;
                    target = v;
                }
            }

            if (target == null) continue;

            // Apply damage over time.
            if (!_hp.TryGetValue(target.Id, out float hp)) hp = StandinHealth;
            hp -= DamagePerSecond * dt;
            _hp[target.Id] = hp;

            if (hp <= 0f)
            {
                target.Alive = false;
                _vehicles.Remove(target.Id);
                _hp.Remove(target.Id);
                destroyed.Add(target.Id);
                GD.Print($"AiDefense: base '{b.Id}' (faction {owner}) destroyed vehicle {target.Id}");
            }
        }

        // Clean up hp for vehicles no longer present.
        foreach (var v in _vehicles.Alive())
        {
            if (!_hp.ContainsKey(v.Id)) _hp[v.Id] = StandinHealth;
        }

        return destroyed;
    }
}
