using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// A gun / cannon. Drop one per gun, position it at the muzzle (nose, wing, or
/// turret), and set <see cref="VehicleComponent.Stats"/> (FireRate, Damage, Range,
/// BulletSpeed, Ammo). The rig adds it to the vehicle's weapon list with its
/// mount position.
///
/// On the client this node draws a barrel pointing +X (the firing direction) and
/// a muzzle-flash node that blinks when <see cref="SetFiring"/> is called. No
/// projectile/damage logic yet — that comes with the combat system.
/// </summary>
[GlobalClass]
public partial class Gun : VehicleComponent
{
    private MeshInstance3D _flash;
    private StandardMaterial3D _flashMat;
    private float _flashTimer;

    public override void _Ready()
    {
        if (Stats.Kind == ComponentKind.Unknown) Stats.Kind = ComponentKind.Gun;
        EnsureDefaults();
        base._Ready();
    }

    /// <summary>
    /// Tell the gun it is firing (the combat system calls this on each shot). The
    /// client flashes the muzzle; the server uses it to spawn projectiles.
    /// </summary>
    public void SetFiring(bool firing)
    {
        if (firing) _flashTimer = 0.05f;
    }

    protected internal override void OnClientReady()
    {
        // Barrel pointing +X (forward = firing direction).
        var barrel = new MeshInstance3D
        {
            Name = "Barrel",
            Mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 3f },
            Position = new Vector3(1.5f, 0f, 0f),
            Rotation = new Vector3(0f, 0f, -Mathf.Pi / 2f),
        };
        barrel.MaterialOverride = CompMat.Std(new Color(0.25f, 0.25f, 0.28f), 0.4f, 0.8f);
        AddChild(barrel);

        // Muzzle flash (hidden until firing).
        _flashMat = CompMat.Emissive(new Color(1f, 0.8f, 0.2f), 0f, 0.3f);
        _flashMat.Transparency = Godot.BaseMaterial3D.TransparencyEnum.Alpha;
        _flashMat.AlbedoColor = new Color(1f, 0.8f, 0.2f, 0.9f);
        _flash = new MeshInstance3D
        {
            Name = "MuzzleFlash",
            Mesh = new SphereMesh { Radius = 0.4f, Height = 0.8f },
            Position = new Vector3(3.1f, 0f, 0f),
            Visible = false,
        };
        _flash.MaterialOverride = _flashMat;
        AddChild(_flash);
    }

    protected internal override void OnClientProcess(double delta)
    {
        if (_flash == null) return;
        if (_flashTimer > 0f)
        {
            _flashTimer -= (float)delta;
            _flash.Visible = true;
            float s = 0.7f + 0.6f * Mathf.Sin(_flashTimer * 200f);
            _flash.Scale = new Vector3(s, s, s);
            CompMat.SetEmission(_flashMat, 3f);
        }
        else
        {
            _flash.Visible = false;
            CompMat.SetEmission(_flashMat, 0f);
        }
    }
}
