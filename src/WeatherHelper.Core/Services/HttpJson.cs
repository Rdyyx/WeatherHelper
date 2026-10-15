using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using WeatherHelper.Json;

namespace WeatherHelper.Services;

/// <summary>
/// 统一的 HTTP + JSON 取数入口。
///
/// 三个关键设计：
/// 1. <b>单个静态 HttpClient</b> —— 每次点击 new 一个会耗尽套接字；
/// 2. <b>HttpClient.Timeout 设为无限</b>，超时交给每次请求自己的 CTS ——
///    否则「超时」和「用户取消」都是同一个 OperationCanceledException，无法区分；
/// 3. <b>失败自动重试一次</b> —— Open-Meteo 实测存在瞬时超时。
/// </summary>
public static class HttpJson
{
    private static readonly HttpClient Http = Create();

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(700);

    /// <summary>默认尝试次数（含首次）。</summary>
    public const int DefaultAttempts = 2;

    /// <summary>地理编码的超时预算（较短，失败后还有主预报和备用源要跑）。</summary>
    public static readonly TimeSpan GeocodeTimeout = TimeSpan.FromSeconds(6);

    /// <summary>主预报的超时预算。</summary>
    public static readonly TimeSpan ForecastTimeout = TimeSpan.FromSeconds(8);

    /// <summary>备用源（wttr.in）的超时预算。</summary>
    public static readonly TimeSpan FallbackTimeout = TimeSpan.FromSeconds(8);

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 4,
            // 刻意不设 UseProxy = false：保留系统代理支持（换取首次请求多约 1 秒的
            // 代理探测开销）。这点延迟由 App 启动时的 WarmUpAsync 在后台消化掉。
        };

        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        // wttr.in 对默认/未知的 User-Agent 会直接返回 503，必须显式设置一个
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WeatherHelper/1.0 (Windows NT 10.0; Win64; x64)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.5");
        return client;
    }

    /// <summary>取 JSON；解析失败按"服务端返回无法识别的数据"处理。</summary>
    public static async Task<JsonDocument> GetJsonAsync(
        string url, TimeSpan timeout, CancellationToken cancellationToken, string? city = null)
    {
        var body = await GetStringAsync(url, timeout, cancellationToken, city).ConfigureAwait(false);
        var document = JsonHelp.Parse(body);
        if (document is null)
            throw WeatherException.BadResponse(city);
        return document;
    }

    /// <summary>取字符串；失败（超时或传输错误）时重试，最多 <paramref name="attempts"/> 次。</summary>
    public static async Task<string> GetStringAsync(
        string url,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? city = null,
        int attempts = DefaultAttempts)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout);

            try
            {
                using var response = await Http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                    .ConfigureAwait(false);

                // 429 / 5xx 视为可重试（wttr.in 负载高时会 503）
                if ((int)response.StatusCode is 429 or >= 500)
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}");

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(attemptCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 是本次请求超时，不是用户取消 —— 可重试
                lastError = new TimeoutException("请求超时");
            }
            catch (HttpRequestException error)
            {
                lastError = error;
            }

            if (attempt < attempts)
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
        }

        // 用户主动取消会以 OperationCanceledException 原样抛出（上面的 when 过滤器排除了它），不会走到这里
        throw WeatherException.FromTransport(lastError!, city);
    }

    /// <summary>
    /// 后台预热连接。
    ///
    /// 实测：一个进程里的<b>第一次</b>请求要付 DNS 解析 + TLS 握手 + 系统代理探测的成本，
    /// 耗时约 2–3 秒；之后同一进程内的请求只要约 350 毫秒。
    /// 界面启动时先在后台跑一次，等用户输完地名、点下「查询」时连接已经是热的。
    ///
    /// 纯属优化，失败不影响任何功能，调用方不必关心结果。
    /// </summary>
    public static async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        string[] urls =
        [
            "https://geocoding-api.open-meteo.com/v1/search?name=beijing&count=1&language=zh&format=json",
            "https://api.open-meteo.com/v1/forecast?latitude=39.9&longitude=116.4&current=temperature_2m",
        ];

        foreach (var url in urls)
        {
            if (cancellationToken.IsCancellationRequested) return;

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));

                using var response = await Http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 预热失败无所谓
            }
        }
    }
}