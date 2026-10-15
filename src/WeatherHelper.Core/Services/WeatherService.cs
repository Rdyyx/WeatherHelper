using WeatherHelper.Models;

namespace WeatherHelper.Services;

/// <summary>
/// 数据源编排：和风天气（仅在配置了 Key 时）→ Open-Meteo → wttr.in。
///
/// 降级时刻意区分对待两类错误：
/// <list type="bullet">
/// <item><b>网络类</b>（<see cref="WeatherErrorKind.Network"/> / <see cref="WeatherErrorKind.Timeout"/> /
/// <see cref="WeatherErrorKind.BadResponse"/> / <see cref="WeatherErrorKind.NotFound"/>）→ 静默降级到下一个源；</item>
/// <item><b>配置类</b>（<see cref="WeatherErrorKind.Config"/>，例如 Key 无效）→ <b>直接抛出</b>。
/// 用户明明填了 Key，却因为 Key 是错的而悄悄改用免费源、还显示"查询成功"，只会让人更困惑。</item>
/// </list>
///
/// 无论走了哪条路，<see cref="WeatherReport.Source"/> 都如实记录实际作答的数据源。
/// </summary>
public sealed class WeatherService : IWeatherService
{
    private readonly AppSettings _settings;
    private readonly OpenMeteoService _openMeteo;
    private readonly WttrInService _wttrIn;

    public WeatherService(
        AppSettings? settings = null,
        string? openMeteoGeocodeBase = null,
        string? openMeteoForecastBase = null,
        string? wttrBase = null)
    {
        _settings = settings ?? AppSettings.Load();
        _openMeteo = new OpenMeteoService(openMeteoGeocodeBase, openMeteoForecastBase);
        _wttrIn = new WttrInService(wttrBase);
    }

    /// <summary>当前配置下的降级链，用于界面上说明"会依次尝试哪些源"。</summary>
    public string Name => _settings.QWeather.IsUsable
        ? "和风天气 → Open-Meteo → wttr.in"
        : "Open-Meteo → wttr.in";

    public async Task<WeatherReport> GetWeatherAsync(string city, CancellationToken cancellationToken)
    {
        var failures = new List<WeatherException>();

        if (_settings.QWeather.IsUsable)
        {
            var qweather = new QWeatherService(
                _settings.QWeather.ApiHost, _settings.QWeather.GeoApiHost, _settings.QWeather.Key);

            try
            {
                return await qweather.GetWeatherAsync(city, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;   // 用户主动取消，不降级
            }
            catch (WeatherException error) when (error.Kind != WeatherErrorKind.Config)
            {
                failures.Add(error);   // 配置类错误不会被这个过滤器接住，会直接往上抛
            }
        }

        try
        {
            return await _openMeteo.GetWeatherAsync(city, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WeatherException error)
        {
            failures.Add(error);
        }

        try
        {
            return await _wttrIn.GetWeatherAsync(city, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WeatherException error)
        {
            failures.Add(error);
        }

        // 全部失败。优先报"未找到"，因为对用户来说这是最有信息量的结论；
        // 否则报第一个（也就是优先级最高那个源的）错误。
        var notFound = failures.FirstOrDefault(error => error.Kind == WeatherErrorKind.NotFound);
        throw notFound ?? failures.FirstOrDefault() ?? WeatherException.BadResponse(city);
    }
}