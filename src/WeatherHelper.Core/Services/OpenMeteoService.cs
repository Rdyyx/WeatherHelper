using System.Globalization;
using System.Text.Json;
using WeatherHelper.Json;
using WeatherHelper.Models;

namespace WeatherHelper.Services;

/// <summary>
/// 主数据源：Open-Meteo。免费、无需 API Key、支持中文地名。
/// 先调地理编码接口把地名解析成经纬度，再调预报接口取实况 + 3 天日预报 + 逐时。
/// </summary>
public sealed class OpenMeteoService : IWeatherService
{
    private const string DefaultGeocodeBase = "https://geocoding-api.open-meteo.com";
    private const string DefaultForecastBase = "https://api.open-meteo.com";

    /// <summary>逐时预报保留的条数。</summary>
    private const int HourlyCount = 24;

    private readonly string _geocodeBase;
    private readonly string _forecastBase;

    /// <param name="geocodeBase">地理编码主机，可覆盖（自检工具用它模拟故障）。</param>
    /// <param name="forecastBase">预报主机，可覆盖。</param>
    public OpenMeteoService(string? geocodeBase = null, string? forecastBase = null)
    {
        _geocodeBase = Trim(geocodeBase) ?? DefaultGeocodeBase;
        _forecastBase = Trim(forecastBase) ?? DefaultForecastBase;
    }

    public string Name => "Open-Meteo（免费、无需 API Key）";

    public async Task<WeatherReport> GetWeatherAsync(string city, CancellationToken cancellationToken)
    {
        var place = await GeocodeAsync(city, cancellationToken).ConfigureAwait(false);
        return await ForecastAsync(place, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>地名 → 经纬度。找不到就抛 <see cref="WeatherException"/>（NotFound）。</summary>
    private async Task<Location> GeocodeAsync(string city, CancellationToken cancellationToken)
    {
        var url = $"{_geocodeBase}/v1/search?name={Uri.EscapeDataString(city)}&count=5&language=zh&format=json";

        using var document = await HttpJson
            .GetJsonAsync(url, HttpJson.GeocodeTimeout, cancellationToken, city)
            .ConfigureAwait(false);

        var root = document.RootElement;

        // 关键：无匹配时 Open-Meteo 连 results 键都不返回，不是返回空数组
        var results = root.Arr("results");
        if (results is null || results.Value.Len() == 0)
            throw WeatherException.NotFound(city);

        var best = results.Value.At(0)!.Value;

        var latitude = best.Num("latitude") ?? throw WeatherException.BadResponse(city);
        var longitude = best.Num("longitude") ?? throw WeatherException.BadResponse(city);

        return new Location(
            Name: best.Str("name") ?? city,
            Admin1: best.Str("admin1"),
            Admin2: best.Str("admin2"),
            Country: best.Str("country") ?? string.Empty,
            Latitude: latitude,
            Longitude: longitude,
            TimeZone: best.Str("timezone"),
            Population: best.Int("population"));
    }

    private async Task<WeatherReport> ForecastAsync(Location place, CancellationToken cancellationToken)
    {
        var latitude = place.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
        var longitude = place.Longitude.ToString("0.####", CultureInfo.InvariantCulture);

        var url = $"{_forecastBase}/v1/forecast"
            + $"?latitude={latitude}&longitude={longitude}"
            + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,"
            + "weather_code,wind_speed_10m,wind_direction_10m,is_day"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max"
            + "&hourly=temperature_2m,weather_code,precipitation_probability"
            + "&timezone=auto&forecast_days=7";

        using var document = await HttpJson
            .GetJsonAsync(url, HttpJson.ForecastTimeout, cancellationToken, place.Name)
            .ConfigureAwait(false);

        var root = document.RootElement;

        var currentNode = root.Prop("current") ?? throw WeatherException.BadResponse(place.Name);
        var current = ParseCurrent(currentNode);
        var days = ParseDaily(root);
        var hours = ParseHourly(root, current.LocalTime);

        return new WeatherReport(place, current, days, hours, Name, DateTimeOffset.Now);
    }

    private static CurrentWeather ParseCurrent(JsonElement node) => new(
        TemperatureC: node.Num("temperature_2m") ?? double.NaN,
        ApparentC: node.Num("apparent_temperature"),
        Humidity: node.Int("relative_humidity_2m"),
        PrecipitationMm: node.Num("precipitation"),
        WindSpeedKmh: node.Num("wind_speed_10m"),
        WindDirectionDeg: node.Num("wind_direction_10m"),
        LocalTime: ParseLocalTime(node.Str("time")),
        IsDay: node.Bool("is_day"),
        WeatherCode: node.Int("weather_code") ?? -1,
        ProviderText: null);

    /// <summary>日预报的各数组长度理论上可能不齐，逐项用 At/IntAt 安全取值（越界返回 null）。</summary>
    private static List<DailyForecast> ParseDaily(JsonElement root)
    {
        var days = new List<DailyForecast>();

        var daily = root.Prop("daily");
        if (daily is null) return days;

        // 注意：daily 是<b>对象</b>（里面装着 time/weather_code 等数组），不是数组，
        // 所以这里必须用 Prop 而不是 Arr。
        var node = daily.Value;
        var times = node.Arr("time");
        if (times is null) return days;

        var codes = node.Arr("weather_code");
        var maxima = node.Arr("temperature_2m_max");
        var minima = node.Arr("temperature_2m_min");
        var sunrises = node.Arr("sunrise");
        var sunsets = node.Arr("sunset");
        var probabilities = node.Arr("precipitation_probability_max");

        var count = times.Value.Len();
        for (var i = 0; i < count; i++)
        {
            if (!DateOnly.TryParse(times.Value.StrAt(i), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            days.Add(new DailyForecast(
                Date: date,
                WeatherCode: codes?.IntAt(i) ?? -1,
                MaxC: maxima?.NumAt(i),
                MinC: minima?.NumAt(i),
                Sunrise: Format.ShortClock(sunrises?.StrAt(i)),
                Sunset: Format.ShortClock(sunsets?.StrAt(i)),
                PrecipProbabilityMax: probabilities?.IntAt(i),
                ProviderText: null));
        }

        return days;
    }

    /// <summary>
    /// 逐时预报。接口返回的是从<b>当天 00:00</b> 起的 72 条，直接铺出来会让人困惑，
    /// 所以从第一个不早于"当前时刻"的整点开始，取 24 条。
    /// </summary>
    private static List<HourlyPoint> ParseHourly(JsonElement root, DateTime? reference)
    {
        var hours = new List<HourlyPoint>();

        // hourly 同样是对象，不是数组
        var hourly = root.Prop("hourly");
        if (hourly is null) return hours;

        var node = hourly.Value;
        var times = node.Arr("time");
        if (times is null) return hours;

        var temperatures = node.Arr("temperature_2m");
        var codes = node.Arr("weather_code");
        var probabilities = node.Arr("precipitation_probability");

        var count = times.Value.Len();

        var start = 0;
        if (reference is not null)
        {
            for (var i = 0; i < count; i++)
            {
                var time = ParseLocalTime(times.Value.StrAt(i));
                if (time is not null && time.Value >= reference.Value)
                {
                    start = i;
                    break;
                }
            }
        }

        for (var i = start; i < count && hours.Count < HourlyCount; i++)
        {
            var time = ParseLocalTime(times.Value.StrAt(i));
            if (time is null) continue;

            hours.Add(new HourlyPoint(
                Time: time.Value,
                TempC: temperatures?.NumAt(i),
                WeatherCode: codes?.IntAt(i) ?? -1,
                PrecipProbability: probabilities?.IntAt(i),
                ProviderText: null));
        }

        return hours;
    }

    /// <summary>
    /// 解析接口返回的时间。注意这些是<b>无时区的当地墙钟时间</b>（timezone=auto），
    /// 解析后要原样展示，不能再做一次时区换算 —— 否则会出现日出时间差一小时的 bug。
    /// </summary>
    private static DateTime? ParseLocalTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string? Trim(string? baseUrl)
        => string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
}