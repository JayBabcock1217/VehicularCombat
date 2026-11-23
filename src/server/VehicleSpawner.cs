using Godot;
using System.Collections.Generic;
using VehicularCombat.Shared;

namespace VehicularCombat.Server;

/// <summary>
/// Spawns vehicle stand-ins. DATA-DRIVEN: it reads the class def from
/// <see cref="VehicleClassRegistry"/> and places the stand-in near a base using
/// the slot choice. It knows nothing about specific classes — adding a 5th class
/// requires no change here (GOALS.md §7.1 "no rework").
/// </summary>
public partial class VehicleSpawner : Node
{
    [Export] private VehicleRegistry _registry;

    /// <summary>Max concurrent stand-ins (Stage 0 sanity cap; real cap is the player count).</summary>
    [Export] public int MaxVehicles { get; set; } = 128;

    /// <summary>Offset distance from a base at which a spawned vehicle appears.</summary>
    [Export] public float SpawnOffset { get; set; } = 25f;

    /// <summary>
    /// Spawn altitude (Y) for AERIAL classes (air/heli), above the ground so the
    /// model is fully visible and not half-buried in the terrain. Planar classes
    /// (ground/naval) spawn at ground level (Y=0). Tune to taste.
    /// </summary>
    [Export] public float AerialSpawnAltitude { get; set; } = 60f;

    private readonly RandomNumberGenerator _rng = new();

    public void Setup(VehicleRegistry registry) => _registry = registry;

    /// <summary>
    /// Spawn a stand-in of <paramref name="cls"/> for <paramref name="faction"/>
    /// near <paramref name="baseDef"/>. <paramref name="slot"/>: 0 = deep (closer
    /// to base), 1 = frontline (offset further out toward center). Returns the
    /// vehicle id, or -1 if at capacity.
    /// </summary>
    public int Spawn(int faction, VehicleClassId cls, BaseDef baseDef, int slot)
    {
        if (_registry.Count >= MaxVehicles) return -1;

        int id = _registry.NextId();

        // Spawn offset: deep = small radius around base; frontline = larger, biased
        // toward the world center (0,0,0) so "frontline" means closer to contested ground.
        float radius = slot == 1 ? SpawnOffset * 2.5f : SpawnOffset;
        Vector2 offset2 = new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f));
        if (offset2.Length() < 0.001f) offset2 = Vector2.Right;
        offset2 = offset2.Normalized() * _rng.RandfRange(radius * 0.4f, radius);

        // Aerial classes (air/heli) spawn at an altitude above the ground so the
        // model isn't half-buried in the terrain; planar classes spawn at Y=0.
        float spawnY = 0f;
        var clsDef = VehicleClassRegistry.Get(cls);
        if (clsDef != null &&
            (clsDef.Domain == VehicleClassDef.MotionDomain.Aerial ||
             cls == VehicleClassId.Air || cls == VehicleClassId.Heli))
        {
            spawnY = AerialSpawnAltitude;
        }

        Vector3 spawnPos = baseDef.Position + new Vector3(offset2.X, spawnY, offset2.Y);

        // Frontline slot biases toward center.
        if (slot == 1)
        {
            Vector3 toCenter = -baseDef.Position;
            if (toCenter.Length() > 0.001f)
            {
                spawnPos += (toCenter.Normalized() * radius * 0.5f);
            }
        }

        var standin = new VehicleStandin
        {
            Name = $"Vehicle_{id}_{cls}",
        };
        AddChild(standin);
        standin.Configure(id, cls, faction, spawnPos);
        _registry.Add(id, standin);

        GD.Print($"VehicleSpawner: spawned vehicle {id} ({cls}) for faction {faction} at {baseDef.Id}");
        return id;
    }
}
