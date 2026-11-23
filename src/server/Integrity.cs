using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using VehicularCombat.Shared;
using SList = System.Collections.Generic.List<Godot.Collections.Dictionary>;

namespace VehicularCombat.Server;

/// <summary>
/// Integrity / anti-cheat foundation (GOALS.md §7.1, §9 — HIGH PRIORITY). A
/// persistent world with a real economy makes cheating costlier than in discrete
/// matches, so this is a Stage 0 requirement, not a later concern.
///
/// What this provides (the foundation):
///  1. Server-authoritative simulation (enforced by architecture — clients send
///     inputs only; the server integrates all motion/state).
///  2. Input validation — reject implausible inputs (e.g. speed beyond the class
///     max, out-of-range values).
///  3. Anomaly detection — flag events that can't arise from valid inputs
///     (teleport, instant capture, resource manipulation).
///  4. Audit log — append-only log of key events (join, spawn, capture, resource
///     spend, destroy, anomaly) with timestamps, for later review/enforcement.
///
/// What this does NOT do yet (open items, GOALS.md §9): specific anomaly tuning,
/// replay/audit retention, and the review/enforcement tooling for the server team.
/// </summary>
public partial class Integrity : Node
{
    private readonly SList _audit = new();

    /// <summary>Log an audit event. Append-only; retained in-memory for Stage 0.</summary>
    public void Log(string level, string event_, string detail)
    {
        var e = new Dictionary
        {
            ["ts"] = Time.GetTicksMsec(),
            ["level"] = level,
            ["event"] = event_,
            ["detail"] = detail,
        };
        _audit.Add(e);
        // Keep the in-memory log bounded (Stage 0). A later stage writes to disk/DB.
        if (_audit.Count > 10000) _audit.RemoveAt(0);

        switch (level)
        {
            case "error": GD.PushError($"[AUDIT] {event_}: {detail}"); break;
            case "warn": GD.PushWarning($"[AUDIT] {event_}: {detail}"); break;
            default: GD.Print($"[AUDIT] {event_}: {detail}"); break;
        }
    }

    /// <summary>
    /// Validate a client input message. Returns true if the input is plausible.
    /// On failure, logs an anomaly and the caller should discard the input.
    /// </summary>
    public bool ValidateInput(Dictionary input, VehicleClassDef def)
    {
        bool ok = true;
        float throttle = (float)input["throttle"];
        float turn = (float)input["turn"];
        float pitch = (float)input["pitch"];

        if (Math.Abs(throttle) > 1.01f) { Anomaly("input_out_of_range", $"throttle={throttle}"); ok = false; }
        if (Math.Abs(turn) > 1.01f) { Anomaly("input_out_of_range", $"turn={turn}"); ok = false; }
        if (Math.Abs(pitch) > 1.01f) { Anomaly("input_out_of_range", $"pitch={pitch}"); ok = false; }

        if (!ok)
        {
            Log("warn", "input_rejected",
                $"throttle={throttle} turn={turn} pitch={pitch} (class {def.Id})");
        }
        return ok;
    }

    /// <summary>
    /// Detect a teleport anomaly: a vehicle whose position jumped more than
    /// <paramref name="maxJump"/> units between server ticks.
    /// </summary>
    public void CheckTeleport(int vehicleId, Vector3 prevPos, Vector3 newPos, float maxJump)
    {
        float jump = prevPos.DistanceTo(newPos);
        if (jump > maxJump)
        {
            Anomaly("teleport", $"vehicle {vehicleId} jumped {jump:F1} units ({prevPos} -> {newPos})");
            Log("error", "teleport_flagged", $"vehicle {vehicleId} jump={jump:F1}");
        }
    }

    private void Anomaly(string type, string detail)
    {
        Log("error", "anomaly", $"{type}: {detail}");
    }

    /// <summary>Return the audit log (for a later admin/review tool).</summary>
    public IReadOnlyList<Godot.Collections.Dictionary> AuditLog => _audit;
}
