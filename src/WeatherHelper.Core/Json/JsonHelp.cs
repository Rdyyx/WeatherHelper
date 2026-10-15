using System.Globalization;
using System.Text.Json;

namespace WeatherHelper.Json;

/// <summary>
/// 安全读取 JSON 的小工具：全部走 <c>TryGetProperty</c>，绝不盲取。
/// 这一点很关键 —— Open-Meteo 在地名无匹配时<b>连 results 键都不返回</b>（而不是返回空数组），
/// 直接 <c>GetProperty("results")</c> 会抛 KeyNotFoundException。
/// </summary>
public static class JsonHelp
{
    /// <summary>取子节点；不存在或不是对象则返回 null。</summary>
    public static JsonElement? Prop(this JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : null;

    public static string? Str(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        if (value is null) return null;
        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null,
        };
    }

    public static double? Num(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        return value is null ? null : ToDouble(value.Value);
    }

    public static int? Int(this JsonElement element, string name)
    {
        var value = element.Num(name);
        return value is null ? null : (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
    }

    public static bool? Bool(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        if (value is null) return null;
        return value.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.Value.GetDouble() != 0,
            _ => null,
        };
    }

    /// <summary>取数组子节点；不存在或不是数组则返回 null。</summary>
    public static JsonElement? Arr(this JsonElement element, string name)
    {
        var value = element.Prop(name);
        if (value is null) return null;
        return value.Value.ValueKind == JsonValueKind.Array ? value : null;
    }

    /// <summary>按下标取数组元素，越界返回 null（各数组长度不齐时靠它兜底）。</summary>
    public static JsonElement? At(this JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Array) return null;
        if (index < 0 || index >= element.GetArrayLength()) return null;
        return element[index];
    }

    public static string? StrAt(this JsonElement element, int index)
    {
        var item = element.At(index);
        if (item is null) return null;
        return item.Value.ValueKind switch
        {
            JsonValueKind.String => item.Value.GetString(),
            JsonValueKind.Number => item.Value.GetRawText(),
            _ => null,
        };
    }

    public static double? NumAt(this JsonElement element, int index)
    {
        var item = element.At(index);
        return item is null ? null : ToDouble(item.Value);
    }

    public static int? IntAt(this JsonElement element, int index)
    {
        var value = element.NumAt(index);
        return value is null ? null : (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
    }

    /// <summary>数组长度；不是数组则为 0。</summary>
    public static int Len(this JsonElement element)
        => element.ValueKind == JsonValueKind.Array ? element.GetArrayLength() : 0;

    /// <summary>解析 JSON；失败返回 null 而不是抛异常（服务端出错时可能回一段 HTML）。</summary>
    public static JsonDocument? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double? ToDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }
}