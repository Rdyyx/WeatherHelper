using WeatherHelper.Models;

namespace WeatherHelper.Services;

/// <summary>一个天气数据源。</summary>
public interface IWeatherService
{
    /// <summary>数据源名称，会原样显示在状态栏，必须如实描述。</summary>
    string Name { get; }

    /// <summary>查询指定地点的天气。失败时抛 <see cref="WeatherException"/>。</summary>
    Task<WeatherReport> GetWeatherAsync(string city, CancellationToken cancellationToken);
}