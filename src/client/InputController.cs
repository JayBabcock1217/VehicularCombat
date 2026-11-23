using Godot;
using Godot.Collections;
using VehicularCombat.Shared;

namespace VehicularCombat.Client;

/// <summary>
/// Captures the player's flight input: the camera (mouse-look) aim AND the
/// throttle. Movement/firing (WASD + LMB) are still disabled for now.
///
/// Mapping:
///   Mouse       -> aim yaw / pitch (from captured mouse motion)
///   Left Shift  -> raise throttle (held, ramps 0..1)
///   Left Ctrl   -> lower throttle (held, ramps 0..1)
///   ESC         -> toggle cursor between captured (in-game) and visible (UI)
///
/// Throttle changes only while InGame (cursor captured), matching how the
/// mouse-look is gated.
/// </summary>
public partial class InputController : Node
{
    // Camera aim (mouse-look). These are the TARGET angles, set directly by the
    // mouse each event. The chase camera reads the SMOOTHED versions (SmoothAimYaw/
    // SmoothAimPitch) so the orbit stays rock-stable when the mouse is still.
    public float AimYaw { get; private set; }
    public float AimPitch { get; private set; }

    // Smoothed aim (what the camera actually orbits). Eased toward AimYaw/AimPitch
    // each frame so per-event mouse deltas don't produce a shivering orbit.
    public float SmoothAimYaw { get; private set; }
    public float SmoothAimPitch { get; private set; }

    // Aim smoothing rate (1/s). Higher = snappier but less stable; ~10 is a good
    // balance for a chase camera (responds to the mouse, no hand-held shiver).
    private const float AimSmooth = 10f;

    // Throttle 0..1. Raised by Left Shift, lowered by Left Ctrl (held), only while
    // InGame. Sent to the server each input tick (see BuildInputMsg).
    public float Throttle { get; private set; }

    // In-game (mouse-look) state. Explicit flag, NOT derived from MouseMode —
    // deriving from MouseMode made look silently dead whenever the mode wasn't
    // exactly Captured at the instant an event arrived.
    public bool InGame { get; private set; }

    // Sensitivity (radians per pixel). Tune here if look feels too fast/slow.
    private const float MouseSensitivity = 0.0026f;

    // Throttle ramp rate (throttle units per second) while the key is held.
    // 0.5 => a full 0..1 ramp takes 2 seconds.
    private const float ThrottleRate = 0.5f;

    public override void _UnhandledInput(InputEvent evt)
    {
        // ESC toggles in-game (captured cursor) <-> UI (visible cursor).
        if (evt is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            ToggleGameMode();
            return;
        }

        // Mouse-look: gated on the EXPLICIT InGame flag (NOT on MouseMode, which is
        // brittle and made look silently dead when the mode wasn't exactly Captured
        // at the instant an event arrived). While InGame the cursor is captured, so
        // Relative is unbounded and the camera can orbit the full 360°.
        if (InGame && evt is InputEventMouseMotion mm)
        {
            // Convention (FPS standard, Option A): mouse RIGHT -> look right,
            // mouse UP -> look up. Signs confirmed correct by the user
            // (yaw in build VC-2026-11-21d, pitch in VC-2026-11-21e).
            AimYaw += (float)mm.Relative.X * MouseSensitivity;
            AimPitch = Mathf.Clamp(AimPitch + (float)mm.Relative.Y * MouseSensitivity, -1.5f, 1.5f);
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // The smoothed aim tracks the raw aim 1:1 (no lag). The camera's
        // POSITION smoothing (CameraRig.TargetSmooth/FollowSmooth) is what
        // makes the orbit fluid — smoothing the ANGLES adds latency and a
        // rubber-band feel. So we just copy: SmoothAim = Aim, every frame.
        SmoothAimYaw = AimYaw;
        SmoothAimPitch = AimPitch;

        // Throttle: ramp up while Left Shift is held, down while Left Ctrl is held.
        // Only while InGame (cursor captured), so the menu can't change throttle.
        if (InGame)
        {
            if (Input.IsKeyPressed(Key.Shift))
                Throttle = Mathf.Clamp(Throttle + ThrottleRate * dt, 0f, 1f);
            if (Input.IsKeyPressed(Key.Ctrl))
                Throttle = Mathf.Clamp(Throttle - ThrottleRate * dt, 0f, 1f);
        }
    }

    public void ToggleGameMode()
    {
        InGame = !InGame;
        Input.MouseMode = InGame ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        GD.Print("Client: mouse " + (InGame ? "locked (captured)" : "released (visible)"));
    }

    /// <summary>Enter in-game mouse-look (capture cursor). Call after spawn.</summary>
    public void EnterGameMode()
    {
        InGame = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        // Snap the smoothed aim to the current target so the camera doesn't ease
        // from a stale angle when (re)entering game mode.
        SmoothAimYaw = AimYaw;
        SmoothAimPitch = AimPitch;
    }

    /// <summary>Leave in-game (show cursor for UI).</summary>
    public void EnterUiMode()
    {
        InGame = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    /// <summary>Build the protocol input message. Throttle (Left Shift / Left Ctrl)
    /// and the camera aim are live; turn/pitch/firing are still disabled (sent as
    /// zero/false) until the flight model adds full 3D controls. The SendInput RPC
    /// + its message shape are kept so the client/server per-node checksum stays in
    /// sync.</summary>
    public Dictionary BuildInputMsg() => NetProtocol.InputMsg(
        throttle: Throttle, turn: 0f, pitch: 0f, aimYaw: AimYaw, aimPitch: AimPitch, firing: false);
}
