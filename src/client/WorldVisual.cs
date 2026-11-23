using Godot;
using System.Collections.Generic;
using VehicularCombat.Shared;

namespace VehicularCombat.Client;

/// <summary>
/// Client-side world rendering: a flat ground plane, a directional light +
/// environment, and a visual marker for each base (tower + hold-radius ring).
/// Everything is built from the Client's mirrored base data (positions/owners),
/// so no extra network data is needed — the server stays headless and sends only
/// the base ownership snapshots the client already receives.
///
/// Base markers are colored by owner faction; the hold ring shows the capture
/// radius. A thin progress ring would come later; Stage 0 uses color + label.
/// </summary>
public partial class WorldVisual : Node3D
{
    private readonly Dictionary<string, Node3D> _baseNodes = new();
    private Node3D _baseRoot;

    public override void _Ready()
    {
        BuildEnvironment();
        _baseRoot = new Node3D { Name = "Bases" };
        AddChild(_baseRoot);
    }

    private void BuildEnvironment()
    {
        // Directional light so the meshes have shading.
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            Rotation = new Vector3(-0.9f, -0.5f, 0f),
            LightEnergy = 1.0f,
        };
        AddChild(sun);

        // Sky + ambient environment (gives the scene a non-black background and
        // ambient fill so dark sides of the boxes aren't invisible).
        var env = new Environment();
        var bg = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.35f, 0.5f, 0.75f),
            SkyHorizonColor = new Color(0.7f, 0.75f, 0.8f),
            GroundHorizonColor = new Color(0.3f, 0.32f, 0.28f),
            GroundBottomColor = new Color(0.2f, 0.2f, 0.18f),
        };
        env.Sky = new Sky { SkyMaterial = bg };
        env.AmbientLightSource = Environment.AmbientSource.Sky;
        env.AmbientLightEnergy = 0.6f;

        var world = new WorldEnvironment { Name = "WorldEnvironment", Environment = env };
        AddChild(world);

        // Ground plane (a large thin box is more reliable than a PlaneMesh for
        // a flat, always-visible floor in Forward+).
        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new BoxMesh
            {
                Size = new Vector3(2400f, 1f, 2400f),
                Material = Std(new Color(0.25f, 0.3f, 0.22f)),
            },
            Position = new Vector3(0f, -0.5f, 0f),
        };
        AddChild(ground);

        // Ground COLLIDER: the visual box above has no physics, so a falling
        // RigidBody3D (the player's aircraft) would pass straight through the
        // floor. Add an infinite floor at Y=0 so the body can rest on the ground
        // (and CCD stops it tunneling through at high speed).
        var groundBody = new StaticBody3D { Name = "GroundCollide" };
        var groundCol = new CollisionShape3D
        {
            Name = "GroundShape",
            Shape = new WorldBoundaryShape3D(), // infinite plane at Y=0 (the body's origin)
        };
        groundBody.AddChild(groundCol);
        AddChild(groundBody);

        // A grid overlay gives a strong sense of motion/scale (the flat green
        // plane alone made it hard to tell if the vehicle was moving).
        AddGridLines();
    }

    /// <summary>Draw a grid of thin lines on the ground so movement is readable.</summary>
    private void AddGridLines()
    {
        const float half = 1000f;    // grid covers -1000..1000
        const float step = 50f;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.6f, 0.55f),
            Roughness = 0.9f,
        };
        var lines = new Node3D { Name = "GridLines" };
        AddChild(lines);
        for (float x = -half; x <= half; x += step)
        {
            lines.AddChild(MakeLine(new Vector3(x, 0.05f, -half), new Vector3(x, 0.05f, half), mat));
            lines.AddChild(MakeLine(new Vector3(-half, 0.05f, x), new Vector3(half, 0.05f, x), mat));
        }
    }

    private MeshInstance3D MakeLine(Vector3 a, Vector3 b, Material m)
    {
        Vector3 d = b - a;
        float len = d.Length();
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.4f, 0.1f, len), Material = m },
            Position = (a + b) * 0.5f,
        };
        // Orient the box's long (Z) axis along the line direction (flat on XZ).
        mi.Rotation = new Vector3(0f, Mathf.Atan2(d.X, d.Z), 0f);
        return mi;
    }

    /// <summary>Reconcile the base markers with the Client's mirrored base data. Call each frame (cheap).</summary>
    public void UpdateBases(Dictionary<string, BaseDef> bases, Dictionary<int, FactionDef> factions)
    {
        var seen = new HashSet<string>();
        foreach (var kv in bases)
        {
            seen.Add(kv.Key);
            var b = kv.Value;
            if (!_baseNodes.TryGetValue(kv.Key, out var node))
            {
                node = new Node3D { Name = "Base_" + kv.Key, Position = b.Position };
                _baseRoot.AddChild(node);
                _baseNodes[kv.Key] = node;
                BuildBase(node, b);
            }
            RecolorBase(node, b, factions);
        }
        foreach (var id in new List<string>(_baseNodes.Keys))
        {
            if (!seen.Contains(id))
            {
                _baseNodes[id].QueueFree();
                _baseNodes.Remove(id);
            }
        }
    }

    private void BuildBase(Node3D node, BaseDef b)
    {
        // Tower (the "infrastructure" itself).
        var tower = new MeshInstance3D
        {
            Name = "Tower",
            Mesh = new BoxMesh { Size = new Vector3(12f, 24f, 12f), Material = Std(Colors.LightGray) },
            Position = new Vector3(0f, 12f, 0f),
        };
        node.AddChild(tower);

        // Hold/capture radius marker: a thin box ring on the ground. (A TorusMesh
        // is too thin to read at range; a 2-unit-thick box reads clearly.)
        var ring = new MeshInstance3D
        {
            Name = "HoldRing",
            Mesh = new BoxMesh
            {
                Size = new Vector3(b.HoldRadius * 2f, 0.6f, b.HoldRadius * 2f),
                Material = Std(Colors.White),
            },
            Position = new Vector3(0f, 0.3f, 0f),
        };
        node.AddChild(ring);
    }

    private void RecolorBase(Node3D node, BaseDef b, Dictionary<int, FactionDef> factions)
    {
        Color c = b.OwnerFaction >= 0 && factions.TryGetValue(b.OwnerFaction, out var f)
            ? f.Color
            : new Color(0.5f, 0.5f, 0.5f); // neutral

        if (node.GetNodeOrNull<MeshInstance3D>("Tower") is MeshInstance3D tower
            && tower.Mesh is BoxMesh)
        {
            var mat = Std(c);
            ((BoxMesh)tower.Mesh).Material = mat;
        }
        if (node.GetNodeOrNull<MeshInstance3D>("HoldRing") is MeshInstance3D ring
            && ring.Mesh is BoxMesh)
        {
            ((BoxMesh)ring.Mesh).Material = Std(c.Darkened(0.15f));
        }
    }

    private static StandardMaterial3D Std(Color c)
    {
        var m = new StandardMaterial3D();
        m.AlbedoColor = c;
        m.Roughness = 0.9f;
        return m;
    }
}
