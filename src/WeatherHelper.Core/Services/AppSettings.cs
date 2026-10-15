using System.Text;
using System.Text.Json;
using WeatherHelper.Json;

namespace WeatherHelper.Services;

/// <summary>和风天气的可选配置。</summary>
public sealed class QWeatherOptions
{
    public const string DefaultApiHost = "https://devapi.qweather.com";
    public const string DefaultGeoApiHost = "https://geoapi.qweather.com";

    public bool Enabled { get; set; }

    /// <summary>天气接口主机。用「专属 API Host」时必须改这里。</summary>
    public string ApiHost { get; set; } = DefaultApiHost;

    /// <summary>地理编码主机。专属 Host 下与 <see cref="ApiHost"/> 相同。</summary>
    public string GeoApiHost { get; set; } = DefaultGeoApiHost;

    public string Key { get; set; } = string.Empty;

    /// <summary>只有显式启用且填了 Key，才认为可用。</summary>
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Key);
}

/// <summary>
/// 应用配置，放在 exe 同目录的 <c>settings.json</c>。
/// 首次运行会自动生成一份带说明的模板。
/// </summary>
public sealed class AppSettings
{
    public QWeatherOptions QWeather { get; set; } = new();

    /// <summary>配置文件路径。单文件发布下 <see cref="AppContext.BaseDirectory"/> 就是 exe 所在目录。</summary>
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    /// <summary>读取配置。任何读取/解析失败都退回默认值，绝不影响主流程。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath, Encoding.UTF8);
                using var document = JsonHelp.Parse(json);
                if (document is not null)
                    return FromJson(document.RootElement);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 配置坏掉也要能查天气
        }

        var settings = new AppSettings();
        settings.WriteTemplateIfMissing();
        return settings;
    }

    private static AppSettings FromJson(JsonElement root)
    {
        var settings = new AppSettings();

        var qweather = root.Prop("qweather");
        if (qweather is not null)
        {
            var node = qweather.Value;
            settings.QWeather.Enabled = node.Bool("enabled") ?? false;
            settings.QWeather.ApiHost = node.Str("apiHost") ?? QWeatherOptions.DefaultApiHost;
            settings.QWeather.GeoApiHost = node.Str("geoApiHost") ?? QWeatherOptions.DefaultGeoApiHost;
            settings.QWeather.Key = node.Str("key") ?? string.Empty;
        }

        return settings;
    }

    /// <summary>首次运行时写一份模板，用户照着填 Key 即可。</summary>
    private void WriteTemplateIfMissing()
    {
        try
        {
            if (File.Exists(FilePath)) return;
            File.WriteAllText(FilePath, Template, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // exe 放在只读目录（如 Program Files）时写不进去，忽略即可
        }
    }

    private const string Template = """
        {
          "_说明": "默认使用免费的 Open-Meteo，无需任何配置。若要优先使用和风天气：把 enabled 改成 true 并填好 key。和风天气已启用「专属 API Host」（形如 https://xxxxx.re.qweatherapi.com），此时 apiHost 与 geoApiHost 都要改成你的专属域名。",

          "qweather": {
            "enabled": false,
            "apiHost": "https://devapi.qweather.com",
            "geoApiHost": "https://geoapi.qweather.com",
            "key": ""
          }
        }
        """;
}