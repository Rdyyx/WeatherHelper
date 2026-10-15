namespace WeatherHelper.Models;

/// <summary>天气的中文描述与单色符号。</summary>
public readonly record struct WeatherVisual(string Text, string Icon);

/// <summary>
/// <b>WMO</b> 天气码 → 中文描述与图标。Open-Meteo 用的是 WMO 码。
/// 参考 Open-Meteo 文档的 "WMO Weather interpretation codes"。
/// 注意：wttr.in 用的是另一套 WWO 码，见 <see cref="WwoCodes"/>。
/// </summary>
public static class WeatherCodes
{
    public static WeatherVisual Describe(int code) => code switch
    {
        0 => new("晴", "☀"),                 // ☀
        1 => new("晴间多云", "⛅"),            // ⛅
        2 => new("多云", "☁"),                // ☁
        3 => new("阴", "☁"),
        45 => new("雾", "☁"),
        48 => new("雾凇", "☁"),
        51 => new("小毛毛雨", "☂"),           // ☂
        53 => new("毛毛雨", "☂"),
        55 => new("大毛毛雨", "☂"),
        56 => new("冻毛毛雨", "☔"),           // ☔
        57 => new("强冻毛毛雨", "☔"),
        61 => new("小雨", "☂"),
        63 => new("中雨", "☂"),
        65 => new("大雨", "☔"),
        66 => new("冻雨", "☔"),
        67 => new("强冻雨", "☔"),
        71 => new("小雪", "❄"),               // ❄
        73 => new("中雪", "❄"),
        75 => new("大雪", "❄"),
        77 => new("雪粒", "❄"),
        80 => new("阵雨", "☂"),
        81 => new("强阵雨", "☂"),
        82 => new("暴雨", "☔"),
        85 => new("阵雪", "❄"),
        86 => new("强阵雪", "❄"),
        95 => new("雷阵雨", "⚡"),             // ⚡
        96 => new("雷阵雨伴冰雹", "⚡"),
        99 => new("强雷阵雨伴冰雹", "⚡"),
        _ => new("未知天气", "？"),            // ？
    };

    private static readonly string[] CompassPoints =
    [
        "北风", "东北偏北风", "东北风", "东北偏东风",
        "东风", "东南偏东风", "东南风", "东南偏南风",
        "南风", "西南偏南风", "西南风", "西南偏西风",
        "西风", "西北偏西风", "西北风", "西北偏北风",
    ];

    /// <summary>风向角度 → 16 方位中文，例如 135° → 东南风。</summary>
    public static string Compass16(double? degrees)
    {
        if (degrees is null) return "风向未知";
        var normalized = degrees.Value % 360;
        if (normalized < 0) normalized += 360;
        var index = (int)Math.Round(normalized / 22.5, MidpointRounding.AwayFromZero) % 16;
        return CompassPoints[index];
    }

    /// <summary>
    /// 由中文描述反推图标。用于那些<b>自己就返回中文文案</b>的数据源
    /// （如和风天气的 <c>now.text</c>），这类源不必再绕一圈天气码。
    /// </summary>
    public static string IconFromText(string? chineseText)
    {
        if (string.IsNullOrWhiteSpace(chineseText)) return UnknownIcon;

        if (chineseText.Contains('雷')) return "⚡";                       // ⚡
        if (chineseText.Contains('雪')) return "❄";                       // ❄
        if (chineseText.Contains('雨')) return "☂";                       // ☂
        if (chineseText.Contains('雾') || chineseText.Contains('霾') ||
            chineseText.Contains('沙') || chineseText.Contains('尘')) return "☁";
        if (chineseText.Contains('晴')) return "☀";                       // ☀
        if (chineseText.Contains('云')) return "⛅";                       // ⛅
        if (chineseText.Contains('阴')) return "☁";                       // ☁
        return UnknownIcon;
    }

    private const string UnknownIcon = "？";  // ？
}