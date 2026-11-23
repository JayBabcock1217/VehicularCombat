using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VehicularCombat.Shared;

/// <summary>
/// JSON helpers bridging System.Text.Json (the only JSON lib in this assembly)
/// and Godot.Collections (the type Godot RPCs serialize). Godot 4.8 does not
/// expose a JSON class in GodotSharp, so we convert explicitly.
/// </summary>
public static class JsonUtil
{
    /// <summary>Serialize a Godot dictionary (JSON-compatible values) to a JSON string.</summary>
    public static string DictToString(Dictionary dict)
    {
        var node = ToJsonNode(dict);
        return node?.ToJsonString() ?? "{}";
    }

    /// <summary>Parse a JSON string into a Godot dictionary. Returns null on failure.</summary>
    public static Dictionary? StringToDict(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj) return FromJsonNode(obj);
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static JsonNode ToJsonNode(object v)
    {
        switch (v)
        {
            case null: return null;
            case Dictionary d: return ToJsonObject(d);
            case Godot.Collections.Array a: return ToJsonArray(a);
            case bool b: return b;
            case int i: return i;
            case long l: return l;
            case float f: return f;
            case double dbl: return dbl;
            case string s: return s;
            case System.Array arr: // float[] / int[] etc.
                var ja = new JsonArray();
                foreach (var e in arr) ja.Add(ToJsonNode(e));
                return ja;
            default:
                // Fallback: try to stringify scalars.
                if (v is sbyte || v is byte || v is short || v is ushort || v is uint || v is ulong)
                    return Convert.ToInt64(v);
                return v.ToString();
        }
    }

    private static JsonObject ToJsonObject(Dictionary d)
    {
        var o = new JsonObject();
        foreach (var key in d.Keys)
        {
            string k = key.AsString();
            o[k] = ToJsonNode(d[key]);
        }
        return o;
    }

    private static JsonArray ToJsonArray(Godot.Collections.Array a)
    {
        var arr = new JsonArray();
        foreach (var e in a) arr.Add(ToJsonNode(e));
        return arr;
    }

    private static Dictionary FromJsonNode(JsonObject obj)
    {
        var d = new Dictionary();
        foreach (var kv in obj)
        {
            d[kv.Key] = FromJsonValue(kv.Value);
        }
        return d;
    }

    private static Variant FromJsonValue(JsonNode node)
    {
        if (node is JsonObject o) return (Variant)FromJsonNode(o);
        if (node is JsonArray a)
        {
            var arr = new Godot.Collections.Array();
            foreach (var e in a) arr.Add(FromJsonValue(e));
            return (Variant)arr;
        }
        if (node is JsonValue jv)
        {
            var raw = jv.GetValueKind();
            if (jv.TryGetValue(out bool b)) return (Variant)b;
            if (jv.TryGetValue(out long l)) return (Variant)l;
            if (jv.TryGetValue(out double dbl)) return (Variant)dbl;
            if (jv.TryGetValue(out string s)) return (Variant)s;
            return (Variant)raw.ToString();
        }
        return (Variant)(string)null;
    }
}
