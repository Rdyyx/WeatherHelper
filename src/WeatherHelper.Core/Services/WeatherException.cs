using System.Net.Sockets;

namespace WeatherHelper.Services;

/// <summary>错误分类。</summary>
public enum WeatherErrorKind
{
    /// <summary>地名未匹配到任何地点。</summary>
    NotFound,

    /// <summary>网络超时。</summary>
    Timeout,

    /// <summary>网络不通（DNS / 套接字 / 被拒）。</summary>
    Network,

    /// <summary>服务端返回了无法识别的数据。</summary>
    BadResponse,

    /// <summary>用户主动取消。</summary>
    Cancelled,

    /// <summary>配置有误（例如开了和风天气但没填 Key）。</summary>
    Config,
}

/// <summary>
/// 面向用户的天气查询异常：<see cref="Exception.Message"/> 一律是可以直接显示在界面上的中文文案，
/// 技术细节只留在 <see cref="Exception.InnerException"/> 里，不外泄堆栈。
/// </summary>
public sealed class WeatherException : Exception
{
    public WeatherErrorKind Kind { get; }

    /// <summary>触发本次异常的查询地名（若有）。</summary>
    public string? City { get; }

    private WeatherException(WeatherErrorKind kind, string message, string? city, Exception? inner)
        : base(message, inner)
    {
        Kind = kind;
        City = city;
    }

    public static WeatherException NotFound(string city)
        => new(WeatherErrorKind.NotFound,
               $"未找到「{city}」，请尝试更完整的地名，例如：北京市、上海市、深圳市、Tokyo。",
               city, null);

    public static WeatherException Timeout(string? city = null, Exception? inner = null)
        => new(WeatherErrorKind.Timeout, "查询超时（网络较慢），请稍后重试。", city, inner);

    public static WeatherException Network(Exception? inner = null, string? city = null)
        => new(WeatherErrorKind.Network, "网络连接失败，请检查网络或代理设置后重试。", city, inner);

    public static WeatherException BadResponse(string? city = null, Exception? inner = null)
        => new(WeatherErrorKind.BadResponse, "天气服务返回了无法识别的数据，请稍后重试。", city, inner);

    public static WeatherException Config(string message)
        => new(WeatherErrorKind.Config, message, null, null);

    /// <summary>把底层传输异常归类成友好文案。</summary>
    public static WeatherException FromTransport(Exception error, string? city = null)
    {
        switch (error)
        {
            case TimeoutException:
            case OperationCanceledException:
                return Timeout(city, error);

            case HttpRequestException http:
                // 连接超时会被包在 HttpRequestException 里，不能一律当成"网络不通"
                if (http.InnerException is TimeoutException or OperationCanceledException or SocketException { SocketErrorCode: SocketError.TimedOut })
                    return Timeout(city, error);
                return Network(error, city);

            default:
                return Network(error, city);
        }
    }
}