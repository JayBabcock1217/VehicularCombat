using Godot;
using VehicularCombat.Shared;

namespace VehicularCombat.Client;

/// <summary>
/// Pre-game / spawn UI: pick a faction, then pick a vehicle TYPE (domain), which
/// populates a dropdown of every VEHICLE of that type available to your faction
/// (from data/vehicles.json), then pick a base + slot to spawn. After spawning,
/// this hides and the HUD + mouse-look take over.
///
/// Selection flow:
///   1. Faction  -> (OptionButton)
///   2. Vehicle type / domain (Plane / Tank / Ship / Heli) -> (OptionButton)
///   3. Vehicle  -> (OptionButton, populated from the catalog for faction + type)
///   4. Base + slot -> (OptionButtons)
///
/// The chosen vehicle's scene is registered on the Client so it renders for the
/// local vehicle. The vehicle's class_id (drives motion/stats/domain) is what the
/// server uses for the stand-in.
/// </summary>
public partial class ConnectUi : CanvasLayer
{
    private Client _client;
    private OptionButton _factionPicker;
    private OptionButton _typePicker;     // domain: air/ground/naval/heli
    private OptionButton _vehiclePicker;  // specific vehicle of the chosen type for the faction
    private OptionButton _basePicker;
    private OptionButton _slotPicker;
    private Button _joinBtn;
    private Button _spawnBtn;
    private Label _status;
    private bool _joined;
    private VehicleType _chosenVehicle;   // the specific vehicle the player picked
    // Parallel list of vehicles for the currently displayed vehicle dropdown
    // (OptionButton item metadata can only hold Variant, not a VehicleType,
    // so we index this list by the dropdown's selected index instead).
    private System.Collections.Generic.List<VehicleType> _vehicleOptions = new();

    // The four domains, shown as friendly names. The key is the type key used in
    // data/vehicles.json; the value is the label shown to the player.
    private static readonly (string Key, string Label)[] Domains =
    {
        ("air", "Plane"),
        ("ground", "Tank"),
        ("naval", "Ship"),
        ("heli", "Heli"),
    };

    public override void _Ready()
    {
        _client = GetParent() as Client;
        if (_client == null)
        {
            GD.PushError("ConnectUi: could not find Client parent");
            return;
        }

        var panel = new VBoxContainer();
        panel.Position = new Vector2(40, 40);
        panel.AddThemeConstantOverride("separation", 12);
        AddChild(panel);

        var title = new Label { Text = "VehicularCombat — Stage 0" };
        panel.AddChild(title);

        var factionLbl = new Label { Text = "1. Faction" };
        panel.AddChild(factionLbl);
        _factionPicker = new OptionButton();
        _factionPicker.ItemSelected += _ => OnFactionChanged();
        panel.AddChild(_factionPicker);

        var typeLbl = new Label { Text = "2. Vehicle type" };
        panel.AddChild(typeLbl);
        _typePicker = new OptionButton();
        _typePicker.ItemSelected += _ => OnTypeChanged();
        panel.AddChild(_typePicker);

        var vehicleLbl = new Label { Text = "3. Vehicle" };
        panel.AddChild(vehicleLbl);
        _vehiclePicker = new OptionButton();
        _vehiclePicker.ItemSelected += _ => OnVehicleChanged();
        panel.AddChild(_vehiclePicker);

        var baseLbl = new Label { Text = "4. Base" };
        panel.AddChild(baseLbl);
        _basePicker = new OptionButton();
        panel.AddChild(_basePicker);

        var slotLbl = new Label { Text = "5. Slot" };
        panel.AddChild(slotLbl);
        _slotPicker = new OptionButton();
        panel.AddChild(_slotPicker);

        _joinBtn = new Button { Text = "Join" };
        _joinBtn.Pressed += OnJoinPressed;
        panel.AddChild(_joinBtn);

        _spawnBtn = new Button { Text = "Spawn Vehicle" };
        _spawnBtn.Disabled = true;
        _spawnBtn.Pressed += OnSpawnPressed;
        panel.AddChild(_spawnBtn);

        _status = new Label { Text = "Waiting for connection…" };
        panel.AddChild(_status);

        PopulateStatic();
    }

    private void PopulateStatic()
    {
        // Factions (static, from the world defaults).
        for (int i = 0; i < Factions.Count; i++)
        {
            int idx = _factionPicker.ItemCount;
            _factionPicker.AddItem(Factions.CreateDefault(i).Name, idx);
            _factionPicker.SetItemMetadata(idx, i); // value == faction id
        }
        _factionPicker.Select(0);

        // Vehicle types (domains) — always the four.
        foreach (var (key, label) in Domains)
        {
            int idx = _typePicker.ItemCount;
            _typePicker.AddItem(label, idx);
            _typePicker.SetItemMetadata(idx, key); // value == type key
        }
        _typePicker.Select(0);

        _slotPicker.AddItem("Deep (own territory)", 0);
        _slotPicker.AddItem("Frontline (closer to fight)", 1);
        _slotPicker.Select(0);

        OnTypeChanged();
    }

    public override void _Process(double delta)
    {
        if (_client == null) return;

        // Populate the base picker from the world snapshot (updates as bases change).
        if (_basePicker.ItemCount != _client.Bases.Count)
        {
            _basePicker.Clear();
            int i = 0;
            foreach (var b in _client.Bases.Values)
            {
                int idx = _basePicker.ItemCount;
                _basePicker.AddItem($"{b.Name} [{OwnerName(b)}]", idx);
                _basePicker.SetItemMetadata(idx, b.Id);
                i++;
            }
            if (_basePicker.ItemCount > 0) _basePicker.Select(0);
        }

        // Reflect connection state.
        _status.Text = _client.Connected
            ? (_joined ? "Joined. Pick a base and spawn." : "Connected. Click 'Join'.")
            : "Connecting to server…";
    }

    private string OwnerName(BaseDef b)
    {
        if (b.OwnerFaction < 0) return "neutral";
        return _client.Factions.TryGetValue(b.OwnerFaction, out var f) ? f.Name : "?";
    }

    // --- Selection cascade ---------------------------------------------------

    private int SelectedFaction() => (int)_factionPicker.GetItemMetadata(_factionPicker.Selected);
    private string SelectedTypeKey() => (string)_typePicker.GetItemMetadata(_typePicker.Selected);

    private void OnFactionChanged()
    {
        // Faction changed: rebuild the vehicle list for the currently selected type.
        OnTypeChanged();
    }

    private void OnTypeChanged()
    {
        int faction = SelectedFaction();
        string typeKey = SelectedTypeKey();
        var vehicles = VehicleTypeRegistry.VehiclesForFaction(faction, typeKey);

        _vehicleOptions = new System.Collections.Generic.List<VehicleType>();
        _vehiclePicker.Clear();
        _chosenVehicle = null;
        if (vehicles.Count == 0)
        {
            _vehiclePicker.AddItem("(none available for this faction/type)", 0);
        }
        else
        {
            int idx = 0;
            foreach (var (_, v) in vehicles)
            {
                _vehicleOptions.Add(v);
                _vehiclePicker.AddItem(v.Name, idx);
                idx++;
            }
            _vehiclePicker.Select(0);
            _chosenVehicle = _vehicleOptions[0];
        }
        _status.Text = _client.Connected
            ? _joined ? "Joined. Pick a base and spawn."
                      : $"Connected. {vehicles.Count} vehicle(s) for {factionName(faction)}/{typeLabel(typeKey)}."
            : "Connecting to server…";
    }

    private void OnVehicleChanged()
    {
        int idx = (int)_vehiclePicker.Selected;
        if (idx >= 0 && idx < _vehicleOptions.Count)
            _chosenVehicle = _vehicleOptions[idx];
    }

    private string factionName(int fid)
        => _client.Factions.TryGetValue(fid, out var f) ? f.Name : Factions.CreateDefault(fid).Name;

    private string typeLabel(string key)
    {
        foreach (var (k, l) in Domains) if (k == key) return l;
        return key;
    }

    // --- Actions -------------------------------------------------------------

    private void OnJoinPressed()
    {
        int faction = SelectedFaction();
        // The class the server uses for motion/stats is the chosen vehicle's class.
        var cls = _chosenVehicle != null ? _chosenVehicle.Class : VehicleClassId.Unknown;
        _client.SendJoin(faction, cls);
        _joined = true;
        _spawnBtn.Disabled = false;
        _status.Text = $"Joined as {factionName(faction)}. Pick a base and spawn.";
    }

    private void OnSpawnPressed()
    {
        if (_chosenVehicle == null) return;
        // Register the chosen vehicle's scene so the client renders it for us.
        _client.RegisterVehicleScene(_chosenVehicle);
        int idx = _basePicker.Selected;
        string baseId = (string)_basePicker.GetItemMetadata(idx);
        int slot = _slotPicker.Selected;
        _client.SendSpawnRequest(baseId, slot);
        // The Client's SpawnResult RPC hides this UI on success.
    }
}
