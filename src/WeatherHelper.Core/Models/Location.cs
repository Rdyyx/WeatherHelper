namespace WeatherHelper.Models;

/// <summary>解析后的地点。</summary>
public sealed record Location(
    string Name,
    string? Admin1,
    string? Admin2,
    string Country,
    double Latitude,
    double Longitude,
    string? TimeZone,
    long? Population)
{
    /// <summary>形如「北京市 · 北京 · 中国」：去重、跳过空段。</summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            Append(parts, Admin1);
            Append(parts, Name);
            Append(parts, Country);
            return parts.Count > 0 ? string.Join(" · ", parts) : Name;
        }
    }

    private static void Append(List<string> parts, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        foreach (var existing in parts)
        {
            if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)) return;
        }
        parts.Add(trimmed);
    }
}