namespace WeatherHelper.Models;

/// <summary>某个整点的预报。</summary>
public sealed record HourlyPoint(
    DateTime Time,
    double? TempC,
    int WeatherCode,
    int? PrecipProbability,
    string? ProviderText)
{
    public string Description =>
        string.IsNullOrWhiteSpace(ProviderText) ? WeatherCodes.Describe(WeatherCode).Text : ProviderText!;

    public string Icon => string.IsNullOrWhiteSpace(ProviderText)
        ? WeatherCodes.Describe(WeatherCode).Icon
        : WeatherCodes.IconFromText(ProviderText);
}