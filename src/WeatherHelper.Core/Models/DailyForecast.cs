namespace WeatherHelper.Models;

/// <summary>某一天的预报。</summary>
public sealed record DailyForecast(
    DateOnly Date,
    int WeatherCode,
    double? MaxC,
    double? MinC,
    string? Sunrise,
    string? Sunset,
    int? PrecipProbabilityMax,
    string? ProviderText)
{
    public string Description =>
        string.IsNullOrWhiteSpace(ProviderText) ? WeatherCodes.Describe(WeatherCode).Text : ProviderText!;

    public string Icon => string.IsNullOrWhiteSpace(ProviderText)
        ? WeatherCodes.Describe(WeatherCode).Icon
        : WeatherCodes.IconFromText(ProviderText);
}