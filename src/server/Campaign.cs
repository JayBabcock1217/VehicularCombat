using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using VehicularCombat.Shared;

namespace VehicularCombat.Server;

/// <summary>
/// Campaign logic: base capture/hold and the frontline. Runs each server tick.
///
/// Capture rule (GOALS.md §5.4): a base is captured when a faction's vehicle(s)
/// remain within its hold radius for <see cref="BaseDef.HoldDuration"/> seconds
/// while no hostile vehicle is inside. Progress resets if a hostile is present or
/// the capturing vehicle leaves. Capture flips ownership and the AI defense.
/// </summary>
public partial class Campaign : Node
{
    [Export] private WorldState _world;
    private VehicleRegistry _vehicles; // live vehicles this tick

    private class HoldState
    {
        public int Faction = -1;
        public float Time = 0f;
    }
    private readonly System.Collections.Generic.Dictionary<string, HoldState> _holds = new();

    public void Setup(WorldState world, VehicleRegistry vehicles)
    {
        _world = world;
        _vehicles = vehicles;
        foreach (var b in world.Bases.Values) _holds[b.Id] = new HoldState();
        _world.BaseCaptured += OnBaseCaptured;
    }

    /// <summary>Advance capture/hold for all bases by one tick. <paramref name="dt"/> in seconds.</summary>
    public void Tick(float dt)
    {
        if (_world == null || _vehicles == null) return;
        foreach (var b in _world.Bases.Values)
        {
            if (!_holds.TryGetValue(b.Id, out var hold)) continue;

            // Count friendly vs hostile vehicles inside the hold radius.
            int friendly = 0, hostile = 0;
            foreach (var v in _vehicles.Alive())
            {
                float d = v.Position.DistanceTo(b.Position);
                if (d <= b.HoldRadius)
                {
                    if (v.Faction == b.OwnerFaction) friendly++;
                    else hostile++;
                }
            }

            // A faction can only make progress if it has presence and the base is
            // not already its own (you don't "recapture" what you hold).
            int candidate = -1;
            if (hostile == 0)
            {
                // Find a faction with presence (any non-owner faction counts as a candidate).
                foreach (var v in _vehicles.Alive())
                {
                    if (v.Position.DistanceTo(b.Position) <= b.HoldRadius && v.Faction != b.OwnerFaction)
                    {
                        candidate = v.Faction;
                        break;
                    }
                }
            }

            if (candidate == -1 || candidate == b.OwnerFaction)
            {
                // No valid capture in progress — decay existing progress.
                if (hold.Time > 0f)
                {
                    hold.Time = Mathf.Max(0f, hold.Time - dt * 2f);
                    _world.SetBaseCapture(b.Id, hold.Faction, hold.Time / b.HoldDuration);
                }
                continue;
            }

            // A valid capture is in progress.
            if (hold.Faction != candidate)
            {
                hold.Faction = candidate;
                hold.Time = 0f;
            }
            hold.Time += dt;
            float progress = Mathf.Clamp(hold.Time / b.HoldDuration, 0f, 1f);
            _world.SetBaseCapture(b.Id, candidate, progress);

            if (hold.Time >= b.HoldDuration)
            {
                _world.SetBaseOwner(b.Id, candidate);
                hold.Faction = -1;
                hold.Time = 0f;
            }
        }
    }

    private void OnBaseCaptured(BaseDef b)
    {
        if (_holds.TryGetValue(b.Id, out var hold))
        {
            hold.Faction = -1;
            hold.Time = 0f;
        }
    }
}
