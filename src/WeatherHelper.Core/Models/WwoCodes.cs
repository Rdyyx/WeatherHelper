namespace WeatherHelper.Models;

/// <summary>
/// <b>WWO</b> 天气码 → 中文描述与图标。wttr.in 用的是 WWO（World Weather Online）码，
/// 和 Open-Meteo 的 WMO 码是两套体系，不能混用。
/// 若码缺失，再用其英文描述做一次兜底匹配。
/// </summary>
public static class WwoCodes
{
    public static WeatherVisual Describe(int? wwoCode, string? englishText)
    {
        if (wwoCode is not null && ByCode.TryGetValue(wwoCode.Value, out var byCode))
            return byCode;

        if (!string.IsNullOrWhiteSpace(englishText))
        {
            var key = englishText.Trim();
            if (ByText.TryGetValue(key, out var byText)) return byText;
            foreach (var pair in ByText)
            {
                if (key.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }
        }

        return new WeatherVisual("未知天气", "？");
    }

    private static readonly Dictionary<int, WeatherVisual> ByCode = new()
    {
        [113] = new("晴", "☀"),
        [116] = new("多云", "⛅"),
        [119] = new("阴", "☁"),
        [122] = new("阴", "☁"),
        [143] = new("薄雾", "☁"),
        [145] = new("雾", "☁"),
        [149] = new("烟霾", "☁"),
        [248] = new("雾", "☁"),
        [260] = new("冻雾", "☁"),
        [176] = new("小雨", "☂"),
        [179] = new("局部有雪", "❄"),
        [182] = new("局部雨夹雪", "☔"),
        [185] = new("局部冻毛毛雨", "☔"),
        [263] = new("小毛毛雨", "☂"),
        [266] = new("毛毛雨", "☂"),
        [281] = new("冻毛毛雨", "☔"),
        [284] = new("强冻毛毛雨", "☔"),
        [293] = new("小雨", "☂"),
        [296] = new("中雨", "☂"),
        [299] = new("中雨", "☂"),
        [302] = new("大雨", "☔"),
        [305] = new("大雨", "☔"),
        [308] = new("暴雨", "☔"),
        [359] = new("暴雨", "☔"),
        [311] = new("冻雨", "☔"),
        [314] = new("强冻雨", "☔"),
        [317] = new("雨夹雪", "☔"),
        [320] = new("雨夹雪", "☔"),
        [323] = new("小雪", "❄"),
        [326] = new("小雪", "❄"),
        [329] = new("小雪", "❄"),
        [332] = new("中雪", "❄"),
        [335] = new("中雪", "❄"),
        [338] = new("大雪", "❄"),
        [350] = new("冰粒", "❄"),
        [353] = new("阵雨", "☂"),
        [356] = new("强阵雨", "☂"),
        [362] = new("雨夹雪阵雨", "☔"),
        [365] = new("雨夹雪阵雨", "☔"),
        [368] = new("阵雪", "❄"),
        [371] = new("强阵雪", "❄"),
        [200] = new("雷阵雨", "⚡"),
        [386] = new("雷阵雨", "⚡"),
        [389] = new("强雷阵雨", "⚡"),
        [227] = new("雷雪", "⚡"),
        [230] = new("暴风雪", "⚡"),
        [392] = new("雷雪", "⚡"),
        [395] = new("强雷雪", "⚡"),
    };

    private static readonly Dictionary<string, WeatherVisual> ByText =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sunny"] = new("晴", "☀"),
            ["Clear"] = new("晴", "☀"),
            ["Partly cloudy"] = new("晴间多云", "⛅"),
            ["Cloudy"] = new("多云", "☁"),
            ["Overcast"] = new("阴", "☁"),
            ["Mist"] = new("薄雾", "☁"),
            ["Fog"] = new("雾", "☁"),
            ["Freezing fog"] = new("冻雾", "☁"),
            ["Smoky haze"] = new("烟霾", "☁"),
            ["Haze"] = new("霾", "☁"),
            ["Patchy rain nearby"] = new("局部有雨", "☂"),
            ["Patchy rain possible"] = new("可能有雨", "☂"),
            ["Patchy light rain"] = new("局部小雨", "☂"),
            ["Light rain"] = new("小雨", "☂"),
            ["Light drizzle"] = new("小毛毛雨", "☂"),
            ["Patchy light drizzle"] = new("局部小毛毛雨", "☂"),
            ["Freezing drizzle"] = new("冻毛毛雨", "☔"),
            ["Heavy freezing drizzle"] = new("强冻毛毛雨", "☔"),
            ["Patchy freezing drizzle possible"] = new("局部冻毛毛雨", "☔"),
            ["Moderate rain"] = new("中雨", "☂"),
            ["Moderate rain at times"] = new("间歇中雨", "☂"),
            ["Heavy rain"] = new("大雨", "☔"),
            ["Heavy rain at times"] = new("间歇大雨", "☔"),
            ["Light freezing rain"] = new("小冻雨", "☔"),
            ["Moderate or heavy freezing rain"] = new("强冻雨", "☔"),
            ["Light sleet"] = new("小雨夹雪", "☔"),
            ["Moderate or heavy sleet"] = new("强雨夹雪", "☔"),
            ["Patchy sleet possible"] = new("局部雨夹雪", "☔"),
            ["Light snow"] = new("小雪", "❄"),
            ["Patchy light snow"] = new("局部小雪", "❄"),
            ["Patchy snow possible"] = new("局部有雪", "❄"),
            ["Patchy moderate snow"] = new("局部中雪", "❄"),
            ["Moderate snow"] = new("中雪", "❄"),
            ["Patchy heavy snow"] = new("局部大雪", "❄"),
            ["Heavy snow"] = new("大雪", "❄"),
            ["Blowing snow"] = new("高吹雪", "❄"),
            ["Blizzard"] = new("暴风雪", "❄"),
            ["Ice pellets"] = new("冰粒", "❄"),
            ["Light rain shower"] = new("小阵雨", "☂"),
            ["Moderate or heavy rain shower"] = new("强阵雨", "☂"),
            ["Torrential rain shower"] = new("暴雨", "☔"),
            ["Light sleet showers"] = new("小雨夹雪阵雨", "☔"),
            ["Moderate or heavy sleet showers"] = new("强雨夹雪阵雨", "☔"),
            ["Light snow showers"] = new("小阵雪", "❄"),
            ["Moderate or heavy snow showers"] = new("强阵雪", "❄"),
            ["Light showers of ice pellets"] = new("小冰粒阵雨", "❄"),
            ["Moderate or heavy showers of ice pellets"] = new("强冰粒阵雨", "❄"),
            ["Thundery outbreaks possible"] = new("可能有雷阵雨", "⚡"),
            ["Patchy light rain with thunder"] = new("雷阵雨", "⚡"),
            ["Moderate or heavy rain with thunder"] = new("强雷阵雨", "⚡"),
            ["Patchy light snow with thunder"] = new("雷雪", "⚡"),
            ["Moderate or heavy snow with thunder"] = new("强雷雪", "⚡"),
            ["Thundery outbreaks in nearby"] = new("附近有雷阵雨", "⚡"),
        };
}