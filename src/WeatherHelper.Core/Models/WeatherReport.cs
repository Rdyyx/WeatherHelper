namespace WeatherHelper.Models;

/// <summary>一次查询的完整结果。</summary>
public sealed record WeatherReport(
    Location Place,
    CurrentWeather Current,
    IReadOnlyList<DailyForecast> Days,
    IReadOnlyList<HourlyPoint> Hours,
    string Source,
    DateTimeOffset FetchedAt);