using Godot;
using VehicularCombat.Shared;

namespace VehicularCombat.Client;

/// <summary>
/// Procedural stand-in visual for a vehicle, built in code (no scene/mesh assets,
/// Stage 0). The shape and color are chosen from the vehicle class and faction.
/// This is a Node3D with MeshInstance3D children, so it is pure rendering — the
/// authoritative motion lives in the snapshot data, not here.
///
/// Orientation: the vehicle's heading is +X (matches MotionState, which turns
/// yaw and computes forward from Basis.FromEuler). So the "nose"/front of each
/// shape points toward +X.
/// 
/// This is a fallback for vehicles that don't have a registered scene.
/// </summary>
public partial class VehicleVisual : Node3D
{
    private MeshInstance3D _body;

    /// <summary>Build the meshes for the given class + faction. Call once, before adding to the tree.</summary>
    public void Build(VehicleClassId cls, int faction)
    {
        var bodyColor = FactionColor(faction);

        _body = new MeshInstance3D { Name = "Body" };
        Mesh mesh = MeshFor(cls, bodyColor);
        _body.Mesh = mesh;
        AddChild(_body);

        // A little accent so the class is readable at range:
        switch (cls)
        {
            case VehicleClassId.Air:
                AddChild(new MeshInstance3D
                {
                    Name = "Tail",
                    Mesh = new BoxMesh { Size = new Vector3(2f, 10f, 4f), Material = Std(bodyColor.Darkened(0.3f)) },
                    Position = new Vector3(-14f, 3f, 0f),
                });
                break;
            case VehicleClassId.Ground:
                // Turret + gun barrel pointing +X (forward).
                AddChild(new MeshInstance3D
                {
                    Name = "Turret",
                    Mesh = new BoxMesh { Size = new Vector3(6f, 3f, 6f), Material = Std(bodyColor.Darkened(0.25f)) },
                    Position = new Vector3(1f, 6f, 0f),
                });
                AddChild(new MeshInstance3D
                {
                    Name = "Barrel",
                    Mesh = new BoxMesh { Size = new Vector3(10f, 1f, 1f), Material = Std(Colors.DimGray) },
                    Position = new Vector3(8f, 7f, 0f),
                });
                break;
            case VehicleClassId.Heli:
                AddChild(new MeshInstance3D
                {
                    Name = "Tail",
                    Mesh = new BoxMesh { Size = new Vector3(16f, 1f, 1f), Material = Std(bodyColor.Darkened(0.3f)) },
                    Position = new Vector3(-10f, 3f, 0f),
                });
                break;
            case VehicleClassId.Naval:
                AddChild(new MeshInstance3D
                {
                    Name = "Deck",
                    Mesh = new BoxMesh { Size = new Vector3(30f, 1.5f, 8f), Material = Std(bodyColor.Lightened(0.2f)) },
                    Position = new Vector3(0f, 3f, 0f),
                });
                break;
        }
    }

    private static Mesh MeshFor(VehicleClassId cls, Color c)
    {
        switch (cls)
        {
            case VehicleClassId.Air:
                return new BoxMesh { Size = new Vector3(24f, 3f, 10f), Material = Std(c) };
            case VehicleClassId.Heli:
                return new BoxMesh { Size = new Vector3(10f, 5f, 4f), Material = Std(c) };
            case VehicleClassId.Ground:
                return new BoxMesh { Size = new Vector3(10f, 4f, 6f), Material = Std(c) };
            case VehicleClassId.Naval:
                return new BoxMesh { Size = new Vector3(40f, 6f, 12f), Material = Std(c) };
            default:
                return new BoxMesh { Size = new Vector3(6f, 6f, 6f), Material = Std(Colors.Gray) };
        }
    }

    /// <summary>Faction color (falls back to a neutral gray for unknown/neutral).</summary>
    public static Color FactionColor(int faction)
    {
        if (faction == 0) return new Color(0.2f, 0.5f, 1f);   // Alpha
        if (faction == 1) return new Color(1f, 0.35f, 0.2f); // Bravo
        return new Color(0.5f, 0.5f, 0.5f);                    // neutral/unknown
    }

    private static StandardMaterial3D Std(Color c)
    {
        var m = new StandardMaterial3D();
        m.AlbedoColor = c;
        m.Roughness = 0.6f;
        return m;
    }
}
