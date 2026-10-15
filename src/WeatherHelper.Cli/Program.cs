using System.Diagnostics;
using System.Globalization;
using System.Text;
using WeatherHelper.Json;
using WeatherHelper.Models;
using WeatherHelper.Services;

namespace WeatherHelper;

/// <summary>
/// 无界面自检工具：和 GUI 复用同一份 <c>WeatherHelper.Core</c>，
/// 用来在写界面之前先把"联网取数到底通不通"验证掉。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var options = CliOptions.Parse(args);
        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        if (options.Json)
            return await RunRawDumpAsync(options).ConfigureAwait(false);

        return await RunQueryAsync(options).ConfigureAwait(false);
    }

    private static async Task<int> RunQueryAsync(CliOptions options)
    {
        IWeatherService service;
        try
        {
            service = BuildService(options);
        }
        catch (WeatherException error)
        {
            Console.Error.WriteLine(error.Message);
            return ExitCodeFor(error.Kind);
        }

        if (options.Repeat <= 1)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var report = await service.GetWeatherAsync(options.City, CancellationToken.None).ConfigureAwait(false);
                stopwatch.Stop();
                Render(report, stopwatch.ElapsedMilliseconds);
                return 0;
            }
            catch (WeatherException error)
            {
                Console.Error.WriteLine(error.Message);
                return ExitCodeFor(error.Kind);
            }
        }

        return await RunRepeatAsync(service, options).ConfigureAwait(false);
    }

    /// <summary>连查 N 次，用来量化瞬时超时率、证明"重试一次"确实吸收得掉。</summary>
    private static async Task<int> RunRepeatAsync(IWeatherService service, CliOptions options)
    {
        var timings = new List<long>();
        var failures = 0;
        string? lastError = null;
        string? source = null;

        for (var i = 1; i <= options.Repeat; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var report = await service.GetWeatherAsync(options.City, CancellationToken.None).ConfigureAwait(false);
                stopwatch.Stop();
                timings.Add(stopwatch.ElapsedMilliseconds);
                source = report.Source;
                Console.WriteLine($"第 {i,2} 次：成功  {report.Place.DisplayName}  {Format.Temp(report.Current.TemperatureC)}  {stopwatch.ElapsedMilliseconds} ms");
            }
            catch (WeatherException error)
            {
                stopwatch.Stop();
                failures++;
                lastError = error.Message;
                Console.WriteLine($"第 {i,2} 次：失败  {error.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"共 {options.Repeat} 次：成功 {timings.Count}，失败 {failures}");

        if (timings.Count > 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"耗时 最快 {timings.Min()} ms / 平均 {timings.Sum() / timings.Count} ms / 最慢 {timings.Max()} ms"));
        }

        if (source is not null) Console.WriteLine($"数据来源：{source}");
        if (failures > 0) Console.Error.WriteLine($"最后一次失败原因：{lastError}");

        return failures == 0 ? 0 : 5;
    }

    private static IWeatherService BuildService(CliOptions options) => options.Source switch
    {
        "openmeteo" => new OpenMeteoService(options.Host, options.Host),
        "wttr" => new WttrInService(options.Host),
        "qweather" => BuildQWeather(options),
        _ => new WeatherService(
                AppSettings.Load(),
                // auto 模式下 --host 只覆盖 Open-Meteo，故意留 wttr.in 不动，
                // 这样才能观察到"主源挂了自动降级"的行为
                options.Host, options.Host, null),
    };

    private static IWeatherService BuildQWeather(CliOptions options)
    {
        var settings = AppSettings.Load();
        if (!settings.QWeather.IsUsable)
        {
            throw WeatherException.Config(
                $"未配置和风天气。请在 {AppSettings.FilePath} 中把 qweather.enabled 设为 true 并填写 key。");
        }

        return new QWeatherService(
            options.Host ?? settings.QWeather.ApiHost,
            options.Host ?? settings.QWeather.GeoApiHost,
            settings.QWeather.Key);
    }

    private static void Render(WeatherReport report, long elapsedMs)
    {
        var current = report.Current;

        Console.WriteLine($"地点：{report.Place.DisplayName}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"坐标：{report.Place.Latitude:0.####}, {report.Place.Longitude:0.####}    时区：{report.Place.TimeZone ?? "未知"}"));
        Console.WriteLine($"数据来源：{report.Source}");
        Console.WriteLine($"查询耗时：{elapsedMs} ms");
        Console.WriteLine();

        Console.WriteLine("【实时天气】");
        Console.WriteLine($"  {current.Icon} {current.Description}");
        Console.WriteLine($"  温度 {Format.Temp(current.TemperatureC)}（体感 {Format.Temp(current.ApparentC)}）");
        Console.WriteLine($"  湿度 {Format.Percent(current.Humidity)}    "
                        + $"降水量 {Format.Millimeters(current.PrecipitationMm)}    "
                        + $"风 {current.WindText}");
        Console.WriteLine($"  观测时间 {Format.Clock(current.LocalTime)}");
        Console.WriteLine();

        Console.WriteLine("【未来 7 天】");
        Console.WriteLine("  " + Pad("日期", 12) + Pad("天气", 12) + Pad("最高", 10) + Pad("最低", 10) + Pad("降水概率", 10) + Pad("日出", 8) + "日落");
        foreach (var day in report.Days)
        {
            Console.WriteLine("  "
                + Pad(Format.DayWithWeekday(day.Date), 12)
                + Pad($"{day.Icon} {day.Description}", 12)
                + Pad(Format.Temp(day.MaxC), 10)
                + Pad(Format.Temp(day.MinC), 10)
                + Pad(Format.Percent(day.PrecipProbabilityMax), 10)
                + Pad(day.Sunrise ?? Format.Dash, 8)
                + (day.Sunset ?? Format.Dash));
        }
        Console.WriteLine();

        Console.WriteLine($"【未来 24 小时】共 {report.Hours.Count} 条");
        Console.WriteLine("  " + Pad("时间", 8) + Pad("天气", 12) + Pad("温度", 10) + "降水概率");
        foreach (var hour in report.Hours)
        {
            Console.WriteLine("  "
                + Pad(Format.Clock(hour.Time), 8)
                + Pad($"{hour.Icon} {hour.Description}", 12)
                + Pad(Format.Temp(hour.TempC), 10)
                + Format.Percent(hour.PrecipProbability));
        }
    }

    /// <summary>直接把原始 JSON 打出来，方便核对字段名和结构。</summary>
    private static async Task<int> RunRawDumpAsync(CliOptions options)
    {
        var city = options.City;
        var token = CancellationToken.None;

        try
        {
            if (options.Source == "wttr")
            {
                var baseUrl = options.Host ?? "https://wttr.in";
                Console.WriteLine(await HttpJson.GetStringAsync(
                    $"{baseUrl}/{Uri.EscapeDataString(city)}?format=j1",
                    HttpJson.FallbackTimeout, token, city).ConfigureAwait(false));
                return 0;
            }

            if (options.Source == "qweather")
            {
                var settings = AppSettings.Load();
                if (!settings.QWeather.IsUsable)
                {
                    Console.Error.WriteLine($"未配置和风天气，请编辑 {AppSettings.FilePath}");
                    return 6;
                }

                var geoHost = options.Host ?? settings.QWeather.GeoApiHost;
                var apiHost = options.Host ?? settings.QWeather.ApiHost;

                Console.WriteLine("--- 地理编码 ---");
                Console.WriteLine(await HttpJson.GetStringAsync(
                    $"{geoHost}/geo/v2/city/lookup?location={Uri.EscapeDataString(city)}&number=1&lang=zh&key={Uri.EscapeDataString(settings.QWeather.Key)}",
                    HttpJson.GeocodeTimeout, token, city).ConfigureAwait(false));

                Console.WriteLine();
                Console.WriteLine("--- 实况 ---");
                Console.WriteLine(await HttpJson.GetStringAsync(
                    $"{apiHost}/v7/weather/now?location={Uri.EscapeDataString(city)}&lang=zh&key={Uri.EscapeDataString(settings.QWeather.Key)}",
                    HttpJson.ForecastTimeout, token, city).ConfigureAwait(false));
                return 0;
            }

            var geocodeBase = options.Host ?? "https://geocoding-api.open-meteo.com";
            var forecastBase = options.Host ?? "https://api.open-meteo.com";

            var geocodeJson = await HttpJson.GetStringAsync(
                $"{geocodeBase}/v1/search?name={Uri.EscapeDataString(city)}&count=5&language=zh&format=json",
                HttpJson.GeocodeTimeout, token, city).ConfigureAwait(false);

            Console.WriteLine("--- 地理编码 ---");
            Console.WriteLine(geocodeJson);

            using var document = JsonHelp.Parse(geocodeJson);
            var best = document?.RootElement.Arr("results")?.At(0);
            if (best is null)
            {
                Console.Error.WriteLine("未找到该地点（地理编码没有返回 results）。");
                return 2;
            }

            var latitude = best.Value.Num("latitude")?.ToString("0.####", CultureInfo.InvariantCulture);
            var longitude = best.Value.Num("longitude")?.ToString("0.####", CultureInfo.InvariantCulture);

            Console.WriteLine();
            Console.WriteLine("--- 预报 ---");
            Console.WriteLine(await HttpJson.GetStringAsync(
                $"{forecastBase}/v1/forecast?latitude={latitude}&longitude={longitude}"
                + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,"
                + "weather_code,wind_speed_10m,wind_direction_10m,is_day"
                + "&daily=weather_code,temperature_2m_max,temperature_2m_min,sunrise,sunset,precipitation_probability_max"
                + "&hourly=temperature_2m,weather_code,precipitation_probability"
                + "&timezone=auto&forecast_days=7",
                HttpJson.ForecastTimeout, token, city).ConfigureAwait(false));

            return 0;
        }
        catch (WeatherException error)
        {
            Console.Error.WriteLine(error.Message);
            return ExitCodeFor(error.Kind);
        }
    }

    private static int ExitCodeFor(WeatherErrorKind kind) => kind switch
    {
        WeatherErrorKind.NotFound => 2,
        WeatherErrorKind.Network => 3,
        WeatherErrorKind.Timeout => 4,
        WeatherErrorKind.BadResponse => 5,
        WeatherErrorKind.Config => 6,
        _ => 1,
    };

    /// <summary>按显示宽度补空格（中文算 2 列），否则中文列会全歪。</summary>
    private static string Pad(string text, int width)
    {
        var padding = width - DisplayWidth(text);
        return padding > 0 ? text + new string(' ', padding) : text + " ";
    }

    private static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var ch in text) width += IsWide(ch) ? 2 : 1;
        return width;
    }

    private static bool IsWide(char ch)
        => ch >= 0x1100 && (
            ch <= 0x115F ||
            ch is (char)0x2329 or (char)0x232A ||
            (ch >= 0x2E80 && ch <= 0xA4CF && ch != 0x303F) ||
            (ch >= 0xAC00 && ch <= 0xD7A3) ||
            (ch >= 0xF900 && ch <= 0xFAFF) ||
            (ch >= 0xFE30 && ch <= 0xFE6F) ||
            (ch >= 0xFF00 && ch <= 0xFF60) ||
            (ch >= 0xFFE0 && ch <= 0xFFE6));

    private static void PrintUsage()
    {
        Console.WriteLine("""
            天气查询助手 · 命令行自检工具

            用法：
              WeatherHelper.Cli <地名> [选项]

            选项：
              --source <auto|openmeteo|wttr|qweather>
                                    选择数据源，默认 auto（和风→Open-Meteo→wttr.in）
              --json                打印原始 JSON，便于核对字段
              --repeat <N>          连查 N 次，统计成功率与耗时分布
              --host <URL>          覆盖所选数据源的主机地址。
                                    auto 模式下只覆盖 Open-Meteo，故意保留 wttr.in，
                                    以便观察"主源失败自动降级"的行为。

            示例：
              WeatherHelper.Cli 北京
              WeatherHelper.Cli 北京 --source wttr
              WeatherHelper.Cli 深圳 --repeat 5
              WeatherHelper.Cli 北京 --host https://10.255.255.1

            退出码：0 成功 / 2 未找到 / 3 网络失败 / 4 超时 / 5 数据异常 / 6 配置有误
            """);
    }
}

internal sealed class CliOptions
{
    public string City { get; private set; } = string.Empty;
    public bool Json { get; private set; }
    public int Repeat { get; private set; } = 1;
    public string? Host { get; private set; }
    public string Source { get; private set; } = "auto";

    public static CliOptions? Parse(string[] args)
    {
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--json":
                    options.Json = true;
                    break;

                case "--source":
                    if (++i >= args.Length) return null;
                    options.Source = args[i].Trim().ToLowerInvariant();
                    if (options.Source is not ("auto" or "openmeteo" or "wttr" or "qweather")) return null;
                    break;

                case "--host":
                    if (++i >= args.Length) return null;
                    options.Host = args[i].Trim().TrimEnd('/');
                    break;

                case "--repeat":
                    if (++i >= args.Length) return null;
                    if (!int.TryParse(args[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var repeat)) return null;
                    options.Repeat = Math.Clamp(repeat, 1, 100);
                    break;

                case "-h":
                case "--help":
                    return null;

                default:
                    if (arg.StartsWith('-')) return null;
                    if (options.City.Length == 0) options.City = arg;
                    else return null;
                    break;
            }
        }

        return options.City.Length == 0 ? null : options;
    }
}