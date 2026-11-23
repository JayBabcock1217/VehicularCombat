using Godot;

namespace VehicularCombat.Shared.Components;

/// <summary>
/// Tiny helper to build StandardMaterial3D without tripping over Godot 4.8 C# API
/// naming (Transparency is BaseMaterial3D.TransparencyEnum, emission uses
/// EmissionEnergyMultiplier, MeshInstance3D uses MaterialOverride, alpha lives in
/// Color.A). Centralized so every component uses the correct API.
/// </summary>
public static class CompMat
{
    /// <summary>An opaque (or translucent) standard material.</summary>
    public static StandardMaterial3D Std(Color color, float roughness = 0.7f, float metallic = 0f)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = metallic,
        };
        return m;
    }

    /// <summary>A translucent standard material (alpha = color.A). Use for beams/plates.</summary>
    public static StandardMaterial3D Translucent(Color color, float roughness = 0.9f, float metallic = 0f)
    {
        var m = Std(color, roughness, metallic);
        m.Transparency = Godot.BaseMaterial3D.TransparencyEnum.Alpha;
        return m;
    }

    /// <summary>An emissive (glowing) standard material. energy 0 = off, higher = brighter.</summary>
    public static StandardMaterial3D Emissive(Color color, float energy, float roughness = 0.5f, float metallic = 0f)
    {
        var m = Std(color, roughness, metallic);
        m.EmissionEnabled = true;
        m.Emission = color;
        m.EmissionEnergyMultiplier = energy;
        return m;
    }

    /// <summary>Set the emission intensity on an existing material (for throttling glow).</summary>
    public static void SetEmission(StandardMaterial3D m, float energy)
    {
        if (m == null) return;
        m.EmissionEnabled = energy > 0.01f;
        m.EmissionEnergyMultiplier = energy;
    }
}
