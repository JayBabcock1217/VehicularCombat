using Godot;

namespace VehicularCombat;

/// <summary>
/// Which role the current Godot instance plays. The same project builds and runs
/// both the dedicated headless server and the client; this flag selects the role.
/// </summary>
public enum Mode
{
    /// <summary>Unknown / not yet resolved (e.g. running in the editor).</summary>
    Unknown,

    /// <summary>Headless dedicated server. No rendering, no local player.</summary>
    Server,

    /// <summary>Client that connects to a dedicated server.</summary>
    Client,
}

/// <summary>
/// Resolves the current <see cref="Mode"/> from command-line arguments and exposes
/// shared networking constants. Kept as a static helper so both the server and
/// client entry points use the same source of truth.
/// </summary>
public static class GameConfig
{
    /// <summary>Default ENet port the dedicated server listens on.</summary>
    public const int DefaultServerPort = 7005;

    /// <summary>Target server logic tick rate (Hz). Benchmark later (GOALS.md §9).</summary>
    public const float ServerTickHz = 20f;

    /// <summary>World-snapshot broadcast cadence (Hz).</summary>
    public const float WorldSnapshotHz = 5f;

    /// <summary>Vehicle-snapshot broadcast cadence (Hz).</summary>
    public const float VehicleSnapshotHz = 20f;

    /// <summary>Input send cadence from client to server (Hz).</summary>
    public const float InputSendHz = 20f;

    /// <summary>
    /// Inspect <c>OS.GetCmdlineUserArgs()</c> and return the selected mode.
    /// <list type="bullet">
    /// <item><c>--server</c> → <see cref="Mode.Server"/></item>
    /// <item><c>--client</c> (or a <c>--host</c>/<c>--port</c>) → <see cref="Mode.Client"/></item>
    /// <item>otherwise → <see cref="Mode.Unknown"/> (editor)</item>
    /// </list>
    /// </summary>
    public static Mode ResolveMode()
    {
        string[] args = OS.GetCmdlineUserArgs();
        bool server = false;
        bool client = false;
        foreach (var a in args)
        {
            switch (a)
            {
                case "--server": server = true; break;
                case "--client": client = true; break;
                case "--host":
                case "--port": client = true; break;
            }
        }
        if (server) return Mode.Server;
        if (client) return Mode.Client;
        return Mode.Unknown;
    }

    /// <summary>Read the <c>--host</c> user arg, defaulting to localhost.</summary>
    public static string GetHost()
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--host") return args[i + 1];
        }
        return "127.0.0.1";
    }

    /// <summary>Read the <c>--port</c> user arg, defaulting to <see cref="DefaultServerPort"/>.</summary>
    public static int GetPort()
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--port" && int.TryParse(args[i + 1], out var p)) return p;
        }
        return DefaultServerPort;
    }
}
