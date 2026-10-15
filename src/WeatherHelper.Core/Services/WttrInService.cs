using System.Globalization;
using System.Text.Json;
using WeatherHelper.Json;
using WeatherHelper.Models;

namespace WeatherHelper.Services;

/// <summary>
/// 备用数据源：wttr.in。无需 API Key，接受中文地名。
///
/// 与 Open-Meteo 的三处关键差异：
/// 1. 天气码是 <b>WWO</b> 码而不是 WMO 码，且数字全是<b>字符串</b>（<c>"17"</c>）；
/// 2. <c>areaName/country/region</c> 是 <c>[{value}]</c> 形式的嵌套数组；
/// 3. 逐时预报只有 <b>3 小时一档</b>（每天 8 个槽位），比 Open-Meteo 粗。
///
/// 另外它的 <c>lang_zh</c> 字段实测为空，<b>不可依赖</b>，中文一律由 <see cref="WwoCodes"/> 本地映射。
/// </summary>
public sealed class WttrInService : IWeatherService
{
    private const string DefaultBase = "https://wttr.in";

    /// <summary>wttr.in 每天只有 8 个 3 小时槽位，24 小时内最多凑 8 条。</summary>
    private const int MaxHourlyPoints = 8;

    private readonly string _base;

    public WttrInService(string? baseUrl = null)
        => _base = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBase : baseUrl.Trim().TrimEnd('/');

    public string Name => "wttr.in（备用源）";

    public async Task<WeatherReport> GetWeatherAsync(string city, CancellationToken cancellationToken)
    {
        var url = $"{_base}/{Uri.EscapeDataString(city)}?format=j1";

        using var document = await HttpJson
            .GetJsonAsync(url, HttpJson.FallbackTimeout, cancellationToken, city)
            .ConfigureAwait(false);

        var root = document.RootElement;

        var currentArray = root.Arr("current_condition");
        var currentNode = currentArray?.At(0) ?? throw WeatherException.BadResponse(city);

        var place = ParsePlace(root, city);
        var current = ParseCurrent(currentNode, root);
        var days = ParseDays(root);
        var hours = ParseHours(root, current.LocalTime);

        return new WeatherReport(place, current, days, hours, Name, DateTimeOffset.Now);
    }

    private static Location ParsePlace(JsonElement root, string requestedCity)
    {
        var area = root.Arr("nearest_area")?.At(0);

        return new Location(
            Name: NestedValue(area, "areaName") ?? requestedCity,
            Admin1: NestedValue(area, "region"),
            Admin2: null,
            Country: NestedValue(area, "country") ?? string.Empty,
            Latitude: area?.Num("latitude") ?? double.NaN,
            Longitude: area?.Num("longitude") ?? double.NaN,
            TimeZone: null,
            Population: null);
    }

    private static CurrentWeather ParseCurrent(JsonElement node, JsonElement root)
    {
        var wwoCode = node.Int("weatherCode");
        var visual = WwoCodes.Describe(wwoCode, NestedValue(node, "weatherDesc"));

        // observation_time 是 "03:30 PM" 形式，且不含日期，用 weather[0].date 补上
        var locationDate = ParseDate(root.Arr("weather")?.At(0)?.Str("date"));

        return new CurrentWeather(
            TemperatureC: node.Num("temp_C") ?? double.NaN,
            ApparentC: node.Num("FeelsLikeC"),
            Humidity: node.Int("humidity"),
            PrecipitationMm: node.Num("precipMM"),
            WindSpeedKmh: node.Num("windspeedKmph"),
            WindDirectionDeg: CompassToDegrees(node.Str("winddir16Point")),
            LocalTime: CombineDateAndClock(locationDate, node.Str("observation_time")),
            IsDay: null,
            // WeatherCode 留 -1：wttr 给的是 WWO 码，塞进这个按 WMO 解读的字段会误导，
            // 中文文案统一通过 ProviderText 走。
            WeatherCode: -1,
            ProviderText: visual.Text);
    }

    private static List<DailyForecast> ParseDays(JsonElement root)
    {
        var days = new List<DailyForecast>();

        var weather = root.Arr("weather");
        if (weather is null) return days;

        for (var i = 0; i < weather.Value.Len(); i++)
        {
            var day = weather.Value.At(i);
            if (day is null) continue;

            var node = day.Value;
            var date = ParseDate(node.Str("date"));
            if (date is null) continue;

            var hourly = node.Arr("hourly");
            var noon = hourly?.At(4);   // 12:00 那个槽位，作为当天的代表
            var visual = WwoCodes.Describe(noon?.Int("weatherCode"), NestedValue(noon, "weatherDesc"));

            var astronomy = node.Arr("astronomy")?.At(0);

            days.Add(new DailyForecast(
                Date: date.Value,
                WeatherCode: -1,
                MaxC: node.Num("maxtempC"),
                MinC: node.Num("mintempC"),
                Sunrise: Format.ShortClock(astronomy?.Str("sunrise")),
                Sunset: Format.ShortClock(astronomy?.Str("sunset")),
                PrecipProbabilityMax: MaxChanceOfRain(hourly),
                ProviderText: visual.Text));
        }

        return days;
    }

    /// <summary>把 3 天 × 8 个槽位摊平成时间序列，再从当前时刻起截取 24 小时内的条目。</summary>
    private static List<HourlyPoint> ParseHours(JsonElement root, DateTime? reference)
    {
        var all = new List<HourlyPoint>();

        var weather = root.Arr("weather");
        if (weather is null) return all;

        for (var d = 0; d < weather.Value.Len(); d++)
        {
            var day = weather.Value.At(d);
            if (day is null) continue;

            var node = day.Value;
            var date = ParseDate(node.Str("date"));
            if (date is null) continue;

            var hourly = node.Arr("hourly");
            if (hourly is null) continue;

            for (var slot = 0; slot < hourly.Value.Len(); slot++)
            {
                var entry = hourly.Value.At(slot);
                if (entry is null) continue;

                var hour = ParseSlotHour(entry.Value.Str("time"), slot);
                var visual = WwoCodes.Describe(entry.Value.Int("weatherCode"), NestedValue(entry, "weatherDesc"));

                all.Add(new HourlyPoint(
                    Time: date.Value.ToDateTime(new TimeOnly(hour, 0)),
                    TempC: entry.Value.Num("tempC"),
                    WeatherCode: -1,
                    PrecipProbability: entry.Value.Int("chanceofrain"),
                    ProviderText: visual.Text));
            }
        }

        if (reference is null)
            return all.Take(MaxHourlyPoints).ToList();

        var from = reference.Value;
        var to = from.AddHours(24);

        var window = new List<HourlyPoint>();
        foreach (var point in all)
        {
            if (point.Time < from || point.Time >= to) continue;
            window.Add(point);
            if (window.Count >= MaxHourlyPoints) break;
        }

        return window;
    }

    /// <summary>取 <c>[{value}]</c> 形式嵌套数组里的第一个 value。</summary>
    private static string? NestedValue(JsonElement? parent, string arrayName)
        => parent?.Arr(arrayName)?.At(0)?.Str("value");

    private static int? MaxChanceOfRain(JsonElement? hourly)
    {
        if (hourly is null) return null;

        int? maximum = null;
        for (var i = 0; i < hourly.Value.Len(); i++)
        {
            var value = hourly.Value.At(i)?.Int("chanceofrain");
            if (value is null) continue;
            if (maximum is null || value > maximum) maximum = value;
        }
        return maximum;
    }

    /// <summary>槽位时间是 <c>"0"/"300"/…/"2100"</c> 这种 HHmm 数字，取小时部分。</summary>
    private static int ParseSlotHour(string? rawTime, int fallbackSlot)
    {
        if (int.TryParse(rawTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            var hours = value / 100;
            if (hours is >= 0 and < 24) return hours;
        }
        return fallbackSlot * 3;
    }

    private static DateOnly? ParseDate(string? text)
        => DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static TimeOnly? ParseClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return time;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            return TimeOnly.FromDateTime(dateTime);
        return null;
    }

    private static DateTime? CombineDateAndClock(DateOnly? date, string? clock)
    {
        if (date is null) return null;
        var time = ParseClock(clock);
        return time is null ? null : date.Value.ToDateTime(time.Value);
    }

    private static readonly Dictionary<string, double> CompassDegrees =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["N"] = 0, ["NNE"] = 22.5, ["NE"] = 45, ["ENE"] = 67.5,
            ["E"] = 90, ["ESE"] = 112.5, ["SE"] = 135, ["SSE"] = 157.5,
            ["S"] = 180, ["SSW"] = 202.5, ["SW"] = 225, ["WSW"] = 247.5,
            ["W"] = 270, ["WNW"] = 292.5, ["NW"] = 315, ["NNW"] = 337.5,
        };

    /// <summary>把 <c>"NNE"</c> 这类 16 方位缩写换算成角度，好让界面上统一显示中文风向。</summary>
    private static double? CompassToDegrees(string? point)
        => point is not null && CompassDegrees.TryGetValue(point.Trim(), out var degrees) ? degrees : null;
}