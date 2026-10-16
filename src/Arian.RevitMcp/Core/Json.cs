using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;

namespace Arian.RevitMcp.Core;

/// <summary>ElementId serialises as a plain number.</summary>
public sealed class ElementIdConverter : JsonConverter<ElementId>
{
    public override ElementId Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        if (reader.TokenType == JsonTokenType.String && long.TryParse(reader.GetString(), out var s)) return new ElementId(s);
        return new ElementId(reader.GetInt64());
    }
    public override void Write(Utf8JsonWriter writer, ElementId value, JsonSerializerOptions o)
    {
        if (value == null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value);
    }
}

/// <summary>XYZ serialises as [x,y,z] in millimetres.</summary>
public sealed class XyzConverter : JsonConverter<XYZ>
{
    public override XYZ Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
    {
        var a = JsonSerializer.Deserialize<double[]>(ref reader, o);
        return U.Pt(a);
    }
    public override void Write(Utf8JsonWriter writer, XYZ v, JsonSerializerOptions o)
    {
        if (v == null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray();
        writer.WriteNumberValue(U.R(U.ToMm(v.X)));
        writer.WriteNumberValue(U.R(U.ToMm(v.Y)));
        writer.WriteNumberValue(U.R(U.ToMm(v.Z)));
        writer.WriteEndArray();
    }
}

public static class J
{
    public static readonly JsonSerializerOptions Opts = Make(false);
    public static readonly JsonSerializerOptions Pretty = Make(true);

    static JsonSerializerOptions Make(bool indent)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
            WriteIndented = indent,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            IncludeFields = true,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        o.Converters.Add(new ElementIdConverter());
        o.Converters.Add(new XyzConverter());
        return o;
    }

    public static JsonNode ToNode(object o)
    {
        if (o == null) return null;
        if (o is JsonNode n) return n;
        return JsonSerializer.SerializeToNode(o, o.GetType(), Opts);
    }

    public static string ToJson(object o, bool indent = false)
        => o is JsonNode n ? n.ToJsonString(indent ? Pretty : Opts) : JsonSerializer.Serialize(o, indent ? Pretty : Opts);

    public static T Get<T>(JsonObject o, string key, T def = default)
    {
        if (o == null || !o.TryGetPropertyValue(key, out var v) || v == null) return def;
        try { return v.Deserialize<T>(Opts); } catch { return def; }
    }
}
