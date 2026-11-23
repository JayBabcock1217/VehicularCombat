using VehicularCombat;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// Small shared helper: tells component/rig code whether it is running on the
/// CLIENT (rendering, local player) or the headless SERVER. Set once at startup
/// by <see cref="VehicularCombat.Bootstrap"/> from <see cref="GameConfig"/>.
///
/// The component system is built so the SAME node classes run on both sides:
/// the server reads their stats (authoritative sim), the client also animates
/// and behaves them. Code that should only run with rendering checks
/// <see cref="IsClient"/>.
/// </summary>
public static class Vehicles
{
    /// <summary>True when running as the (rendering) client. False on the headless server.</summary>
    public static bool IsClient { get; private set; }

    /// <summary>Set once at startup from the resolved mode. Call from Bootstrap._Ready.</summary>
    public static void Init(Mode mode)
    {
        IsClient = mode == Mode.Client;
    }
}
