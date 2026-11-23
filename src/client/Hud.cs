using Godot;
using VehicularCombat.Shared;

namespace VehicularCombat.Client;

/// <summary>
/// In-game HUD: shows the player's faction, the faction resource pool, base
/// ownership/capture status, and a hint line — plus an AIRCRAFT STAT block in the
/// upper-left (throttle, velocity, altitude) updated each frame from the local
/// vehicle. Stage 0 is intentionally minimal.
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label _factionLabel;
    private Label _poolLabel;
    private Label _basesLabel;
    private Label _hintLabel;

    // Aircraft stat block (upper-left): throttle, velocity, altitude, thrust.
    private Label _throttleLabel;
    private Label _velocityLabel;
    private Label _altitudeLabel;
    private Label _thrustLabel;

    private Client _client;
    private int _localFaction;
    private VehicleClassId _localClass;

    public void Setup(Client client, int faction, VehicleClassId cls)
    {
        _client = client;
        _localFaction = faction;
        _localClass = cls;

        // Idempotent: only build the labels once. Setup is called at startup AND
        // again on Join (to update the faction/class) — without this guard the
        // labels would be re-added and stack on top of each other.
        if (_factionLabel != null) return;

        // ------------------------------------------------------------------
        // GAME-INFO block — TOP-RIGHT, ~50% smaller. Each label is ANCHORED to the
        // top-right corner (AnchorLeft/Right=1, AnchorTop/Bottom=0) with negative
        // offsets, so its top-right stays pinned to the screen's top-right at ANY
        // resolution (no fixed pixel x). Font sizes are halved from the old values.
        // ------------------------------------------------------------------
        const int Margin = 16;     // px inset from the right & top edges
        const int BoxW = 520;      // reserved width for the (multi-line) base list
        _factionLabel = TopRightLabel(16, Margin, 0, BoxW);
        _poolLabel = TopRightLabel(12, Margin, 24, BoxW);
        _basesLabel = TopRightLabel(9, Margin, 48, BoxW);
        _hintLabel = TopRightLabel(8, Margin, 220, BoxW);
        _hintLabel.Text = "Mouse look · LShift throttle up · LCtrl throttle down · ESC release mouse";
        AddChild(_factionLabel);
        AddChild(_poolLabel);
        AddChild(_basesLabel);
        AddChild(_hintLabel);

        // ------------------------------------------------------------------
        // AIRCRAFT-STAT block — UPPER-LEFT: throttle, velocity, altitude, thrust.
        // ------------------------------------------------------------------
        _throttleLabel = MakeLabel(0, 120, 22);
        _velocityLabel = MakeLabel(0, 150, 22);
        _altitudeLabel = MakeLabel(0, 180, 22);
        _thrustLabel = MakeLabel(0, 210, 18);
        AddChild(_throttleLabel);
        AddChild(_velocityLabel);
        AddChild(_altitudeLabel);
        AddChild(_thrustLabel);
    }

    private Label MakeLabel(float x, float y, int size)
    {
        var l = new Label();
        l.Position = new Vector2(x, y);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Colors.White);
        return l;
    }

    /// <summary>
    /// A label anchored to the TOP-RIGHT corner of the viewport. Its top-right
    /// stays pinned to the screen's top-right at any resolution.
    ///   <paramref name="size"/>      — font size (px).
    ///   <paramref name="margin"/>    — inset from the right & top edges (px).
    ///   <paramref name="topOffset"/> — extra distance down from the top (px).
    ///   <paramref name="width"/>     — the label's reserved width (px); text is
    ///                                   right-aligned within it.
    /// </summary>
    private Label TopRightLabel(int size, int margin, int topOffset, int width)
    {
        var l = new Label();
        // Anchor the label to the top-right corner of the parent (the viewport).
        l.AnchorTop = 0f;
        l.AnchorRight = 1f;
        l.AnchorBottom = 0f;
        l.AnchorLeft = 1f;
        // Offsets are RELATIVE to the anchors: with the right anchor at the screen's
        // right edge, a negative OffsetRight pulls the right edge IN by `margin`,
        // and OffsetLeft = OffsetRight - width gives the label its width.
        l.OffsetRight = -margin;
        l.OffsetLeft = -margin - width;
        l.OffsetTop = margin + topOffset;
        l.OffsetBottom = l.OffsetTop + size * 4f; // generous height for multi-line
        l.HorizontalAlignment = HorizontalAlignment.Right;
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Colors.White);
        return l;
    }

    public override void _Process(double delta)
    {
        if (_client == null) return;

        if (_client.Factions.TryGetValue(_localFaction, out var f))
        {
            _factionLabel.Text = $"Faction: {f.Name}";
            _factionLabel.AddThemeColorOverride("font_color", f.Color);
            _poolLabel.Text = $"Resource pool: {f.ResourcePool:F0}";
        }

        // Base status line.
        var sb = new System.Text.StringBuilder();
        sb.Append("Bases: ");
        foreach (var b in _client.Bases.Values)
        {
            string owner = b.OwnerFaction >= 0 ? _client.Factions.TryGetValue(b.OwnerFaction, out var fo) ? fo.Name : b.OwnerFaction.ToString() : "neutral";
            string cap = b.CaptureProgress > 0.01f ? $" (capturing {b.CaptureProgress * 100:F0}%)" : "";
            sb.AppendLine($"{b.Name}: {owner}{cap}");
        }
        _basesLabel.Text = sb.ToString();

        // Aircraft stat block (upper-left): throttle, velocity, altitude, thrust.
        // Read live from the local vehicle (rigid body) for frame-accuracy.
        UpdateAircraftStats();
    }

    private void UpdateAircraftStats()
    {
        float throttle = _client.LocalThrottle;
        Vector3 vel = _client.LocalVelocity;
        Vector3 pos = _client.LocalPosition;
        float speedMs = vel.Length();
        float altitude = pos.Y;

        // Throttle: percent (0..100).
        _throttleLabel.Text = $"Throttle: {throttle * 100f:F0}%";

        // Velocity: m/s and knots (1 m/s = 1.94384 kn).
        _velocityLabel.Text = $"Speed: {speedMs:F0} m/s  ({speedMs * 1.94384f:F0} kn)";

        // Altitude: meters and feet (1 m = 3.28084 ft).
        _altitudeLabel.Text = $"Altitude: {altitude:F0} m  ({altitude * 3.28084f:F0} ft)";

        // Thrust: current total thrust from the flight model (N), or 0 if none.
        var fm = _client.LocalFlightModel;
        float thrust = fm != null ? fm.CurrentThrust : 0f;
        _thrustLabel.Text = $"Thrust: {thrust:F0} N";
    }
}
