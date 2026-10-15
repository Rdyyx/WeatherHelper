namespace WeatherHelper.Models;

/// <summary>当前实况。</summary>
public sealed record CurrentWeather(
    double TemperatureC,
    double? ApparentC,
    int? Humidity,
    double? PrecipitationMm,
    double? WindSpeedKmh,
    double? WindDirectionDeg,
    DateTime? LocalTime,
    bool? IsDay,
    int WeatherCode,
    string? ProviderText)
{
    /// <summary>中文天气描述。和风天气会直接给出中文，优先使用。</summary>
    public string Description =>
        string.IsNullOrWhiteSpace(ProviderText) ? WeatherCodes.Describe(WeatherCode).Text : ProviderText!;

    /// <summary>
    /// 单色天气符号（WinForms 的 GDI+ 不渲染彩色 emoji，故用 Segoe UI Symbol 字符）。
    /// 数据源若自带中文文案（和风天气），就直接由文案推图标，不必再绕天气码。
    /// </summary>
    public string Icon => string.IsNullOrWhiteSpace(ProviderText)
        ? WeatherCodes.Describe(WeatherCode).Icon
        : WeatherCodes.IconFromText(ProviderText);

    /// <summary>形如「东南风 12.3 km/h」；缺数据时为「—」。</summary>
    public string WindText
    {
        get
        {
            var direction = WeatherCodes.Compass16(WindDirectionDeg);
            if (WindSpeedKmh is null) return direction;
            return $"{direction} {WindSpeedKmh.Value:0.#} km/h";
        }
    }
}