using Godot;
using VehicularCombat.Shared;

namespace VehicularCombat.Server;

/// <summary>
/// The team resource economy (GOALS.md §5.2 / §5.3): each faction's shared pool
/// generates over time at a rate = base rate + generation from captured
/// infrastructure. Spawning a vehicle spends from that pool. There is no personal
/// credit system — the pool is faction-wide and funds spawning.
/// </summary>
public partial class Economy : Node
{
    [Export] private WorldState _world;

    /// <summary>Cap on a faction's pool to prevent unbounded accumulation (tuning, GOALS.md §9).</summary>
    [Export] public float PoolCap { get; set; } = 100000f;

    public void Setup(WorldState world) => _world = world;

    /// <summary>Advance generation for all factions by one tick. <paramref name="dt"/> in seconds.</summary>
    public void Tick(float dt)
    {
        if (_world == null) return;
        for (int i = 0; i < _world.Factions.Length; i++)
        {
            float rate = _world.GenerationRateFor(i);
            if (rate <= 0f) continue;
            var f = _world.GetFaction(i);
            if (f == null) continue;
            if (f.ResourcePool >= PoolCap) continue;
            _world.AddToPool(i, rate * dt);
        }
    }

    /// <summary>
    /// Attempt to spawn a vehicle of <paramref name="cls"/> for <paramref name="faction"/>
    /// from base <paramref name="baseId"/>. Deducts the class spawn cost on success.
    /// Returns the result code (see NetProtocol.SpawnErr*) and, on success, the
    /// spawned vehicle id.
    /// </summary>
    public (int code, int vehicleId) TrySpawn(
        WorldState world,
        VehicleSpawner spawner,
        int faction,
        VehicleClassId cls,
        string baseId,
        int slot)
    {
        var f = world.GetFaction(faction);
        if (f == null) return (NetProtocol.SpawnErrBadFaction, -1);

        var baseDef = world.GetBase(baseId);
        if (baseDef == null) return (NetProtocol.SpawnErrBadBase, -1);

        if (!VehicleClassRegistry.TryGet(cls, out var def))
            return (NetProtocol.SpawnErrBadClass, -1); // unknown/invalid class id

        // Only spawn at a base your faction owns (or a neutral base you're contesting).
        if (baseDef.OwnerFaction != faction && baseDef.OwnerFaction != -1)
            return (NetProtocol.SpawnErrBadBase, -1);

        // Pay the cost from the faction pool.
        if (!world.TrySpend(faction, def.SpawnCost))
            return (NetProtocol.SpawnErrInsufficientResources, -1);

        // Ask the spawner to create the stand-in. If it can't (no free slot), refund.
        int vehicleId = spawner.Spawn(faction, cls, baseDef, slot);
        if (vehicleId < 0)
        {
            world.AddToPool(faction, def.SpawnCost); // refund
            return (NetProtocol.SpawnErrNoFreeSlot, -1);
        }

        return (NetProtocol.SpawnOk, vehicleId);
    }
}
