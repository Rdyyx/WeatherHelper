using System.Globalization;
using System.Text.Json;
using WeatherHelper.Json;
using WeatherHelper.Models;

namespace WeatherHelper.Services;

/// <summary>
/// 可选数据源：和风天气。需要在 <c>settings.json</c> 里填了 Key 才会启用。
///
/// 两个注意点：
/// 1. 和风天气已改为「专属 API Host」（形如 <c>https://xxxxx.re.qweatherapi.com</c>），
///    老域名 <c>devapi.qweather.com</c> 的地理编码在另一个域 <c>geoapi.qweather.com</c>，
///    所以两个 Host 必须分开配置，写死任何一个都会失效；
/// 2. 它的 <c>text</c> / <c>windDir</c> 字段<b>本身就是中文</b>，直接塞进 ProviderText 即可，
///    不需要再维护一张"和风图标码 → WMO 码"的映射表。
/// </summary>
public sealed class QWeatherService : IWeatherService
{
    private readonly string _apiHost;
    private readonly string _geoHost;
    private readonly string _key;

    public QWeatherService(string apiHost, string geoApiHost, string key)
    {
        _apiHost = TrimHost(apiHost);
        _geoHost = TrimHost(geoApiHost);
        _key = key.Trim();
    }

    public string Name => "和风天气";

    public async Task<WeatherReport> GetWeatherAsync(string city, CancellationToken cancellationToken)
    {
        var place = await LookupAsync(city, cancellationToken).ConfigureAwait(false);
        return await FetchAsync(place, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>地名 → 经纬度。和风天气要求 location 参数是「经度,纬度」（经度在前）。</summary>
    private async Task<Location> LookupAsync(string city, CancellationToken cancellationToken)
    {
        var url = $"{_geoHost}/geo/v2/city/lookup"
            + $"?location={Uri.EscapeDataString(city)}&number=1&lang=zh"
            + $"&key={Uri.EscapeDataString(_key)}";

        using var document = await HttpJson
            .GetJsonAsync(url, HttpJson.GeocodeTimeout, cancellationToken, city)
            .ConfigureAwait(false);

        var root = document.RootElement;
        EnsureSuccessCode(root, city);

        var locations = root.Arr("location");
        if (locations is null || locations.Value.Len() == 0)
            throw WeatherException.NotFound(city);

        var best = locations.Value.At(0)!.Value;

        return new Location(
            Name: best.Str("name") ?? city,
            Admin1: best.Str("adm1"),
            Admin2: best.Str("adm2"),
            Country: best.Str("country") ?? string.Empty,
            Latitude: best.Num("lat") ?? throw WeatherException.BadResponse(city),
            Longitude: best.Num("lon") ?? throw WeatherException.BadResponse(city),
            TimeZone: best.Str("tz"),
            Population: null);
    }

    private async Task<WeatherReport> FetchAsync(Location place, CancellationToken cancellationToken)
    {
        var location = string.Create(CultureInfo.InvariantCulture,
            $"{place.Longitude:0.##},{place.Latitude:0.##}");

        var nowRoot = await GetAsync("/v7/weather/now", location, place.Name, cancellationToken).ConfigureAwait(false);
        var dailyRoot = await GetAsync("/v7/weather/3d", location, place.Name, cancellationToken).ConfigureAwait(false);
        var hourlyRoot = await GetAsync("/v7/weather/24h", location, place.Name, cancellationToken).ConfigureAwait(false);

        var current = ParseCurrent(nowRoot, place.Name);
        var days = ParseDays(dailyRoot, place.Name);
        var hours = ParseHours(hourlyRoot, place.Name, current.LocalTime);

        return new WeatherReport(place, current, days, hours, Name, DateTimeOffset.Now);
    }

    private async Task<JsonElement> GetAsync(string path, string location, string city, CancellationToken cancellationToken)
    {
        var url = $"{_apiHost}{path}?location={location}&lang=zh&key={Uri.EscapeDataString(_key)}";
        var document = await HttpJson
            .GetJsonAsync(url, HttpJson.ForecastTimeout, cancellationToken, city)
            .ConfigureAwait(false);

        // JsonDocument 要活到读取结束，所以这里返回一个克隆的 JsonElement 并立刻释放文档
        using (document)
        {
            var root = document.RootElement;
            EnsureSuccessCode(root, city);
            return root.Clone();
        }
    }

    private static CurrentWeather ParseCurrent(JsonElement root, string city)
    {
        var now = root.Prop("now") ?? throw WeatherException.BadResponse(city);

        return new CurrentWeather(
            TemperatureC: now.Num("temp") ?? double.NaN,
            ApparentC: now.Num("feelsLike"),
            Humidity: now.Int("humidity"),
            PrecipitationMm: now.Num("precip"),
            WindSpeedKmh: now.Num("windSpeed"),
            WindDirectionDeg: now.Num("wind360"),
            LocalTime: ParseWallClock(now.Str("obsTime")),
            IsDay: null,
            WeatherCode: -1,   // 和风自带中文文案，走 ProviderText
            ProviderText: now.Str("text"));
    }

    private static List<DailyForecast> ParseDays(JsonElement root, string city)
    {
        var days = new List<DailyForecast>();

        var daily = root.Arr("daily") ?? throw WeatherException.BadResponse(city);
        for (var i = 0; i < daily.Len(); i++)
        {
            var node = daily.At(i);
            if (node is null) continue;

            if (!DateOnly.TryParse(node.Value.Str("fxDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            days.Add(new DailyForecast(
                Date: date,
                WeatherCode: -1,
                MaxC: node.Value.Num("tempMax"),
                MinC: node.Value.Num("tempMin"),
                Sunrise: node.Value.Str("sunrise"),
                Sunset: node.Value.Str("sunset"),
                // 和风 3 天预报不提供降水概率，如实留空（界面显示「—」）
                PrecipProbabilityMax: null,
                ProviderText: node.Value.Str("textDay")));
        }

        return days;
    }

    private static List<HourlyPoint> ParseHours(JsonElement root, string city, DateTime? reference)
    {
        var hours = new List<HourlyPoint>();

        var hourly = root.Arr("hourly") ?? throw WeatherException.BadResponse(city);
        for (var i = 0; i < hourly.Len(); i++)
        {
            var node = hourly.At(i);
            if (node is null) continue;

            var time = ParseWallClock(node.Value.Str("fxTime"));
            if (time is null) continue;
            if (reference is not null && time.Value < reference.Value) continue;

            hours.Add(new HourlyPoint(
                Time: time.Value,
                TempC: node.Value.Num("temp"),
                WeatherCode: -1,
                PrecipProbability: node.Value.Int("pop"),
                ProviderText: node.Value.Str("text")));
        }

        return hours;
    }

    /// <summary>
    /// 解析和风返回的时间（形如 <c>2026-10-14T23:30+08:00</c>，<b>带时区偏移</b>）。
    /// 取 <see cref="DateTimeOffset.DateTime"/> 拿到地点当地的墙钟时间，
    /// 不做时区换算 —— 与 Open-Meteo 的展示口径保持一致。
    /// </summary>
    private static DateTime? ParseWallClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.DateTime
            : null;
    }

    /// <summary>按和风的 <c>code</c> 字段判定成败。</summary>
    private static void EnsureSuccessCode(JsonElement root, string city)
    {
        var code = root.Str("code");

        switch (code)
        {
            case "200":
                return;

            case "204":
                throw WeatherException.NotFound(city);

            case "401":
            case "403":
                throw WeatherException.Config(
                    "和风天气 API Key 无效或没有权限，请检查 settings.json 里的 qweather.key 与 apiHost。");

            case "402":
                throw WeatherException.Config("和风天气的 API 调用额度已用尽，请充值或改用免费数据源。");

            case "404":
                throw WeatherException.NotFound(city);

            default:
                throw WeatherException.BadResponse(city);
        }
    }

    private static string TrimHost(string host)
        => string.IsNullOrWhiteSpace(host) ? string.Empty : host.Trim().TrimEnd('/');
}