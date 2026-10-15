using System.Diagnostics;
using WeatherHelper.Models;
using WeatherHelper.Services;

namespace WeatherHelper.Ui;

/// <summary>
/// 唯一的联网入口。桌宠和主窗口都只是它的"视图"：谁都不自己发请求，
/// 都订阅 <see cref="Changed"/> 然后照着这里的状态重绘。
///
/// 这样"在弹窗里换了城市、桌宠跟着变"是自动成立的，两个窗口之间不需要任何传值；
/// 30 分钟的自动刷新也不必依赖某个窗口开着 —— 弹窗可能一直不打开。
///
/// <see cref="QueryAsync"/> 绝不外抛异常，调用方可以放心 await。
/// </summary>
internal sealed class WeatherHub : IDisposable
{
    /// <summary>自动刷新间隔。桌宠是常驻的，所以默认就一直转着。</summary>
    private const int AutoRefreshIntervalMs = 30 * 60 * 1000;   // 30 分钟

    private readonly WeatherService _service = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = AutoRefreshIntervalMs };

    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _autoRefreshEnabled;

    /// <summary>
    /// 最近一次查询用的地名，保持用户输入的写法。自动刷新重查的是它而不是
    /// <c>LastReport.Place.Name</c> —— 后者是数据源给出的规范名（常常是英文），
    /// 拿它去重查会让历史记录里平白多出一条「Beijing」，和历史里原有的「北京市」并排。
    /// </summary>
    private string? _lastCity;

    public WeatherHub(bool autoRefreshEnabled)
    {
        _autoRefreshEnabled = autoRefreshEnabled;
        History = HistoryStore.Load();
        _timer.Tick += (_, _) => RefreshLast();
    }

    public WeatherReport? LastReport { get; private set; }

    public bool IsBusy => _busy;

    /// <summary>上次**成功**查询的耗时（毫秒）；还没成功过就是 0。</summary>
    public long LastElapsedMs { get; private set; }

    /// <summary>可以直接显示给用户的状态文案。</summary>
    public string LastStatus { get; private set; } = "就绪。";

    public bool LastStatusIsError { get; private set; }

    /// <summary>最近查询过的地点，最新的在前。主窗口的下拉框用它。</summary>
    public IReadOnlyList<string> History { get; private set; } = [];

    /// <summary>当前配置下的降级链，主窗口状态栏用它。</summary>
    public string SourceName => _service.Name;

    /// <summary>状态有任何变化（开始查、查完、查失败）就触发一次，界面据此整体重绘。</summary>
    public event EventHandler? Changed;

    /// <summary>自动刷新开关。改了会立刻生效并落盘。</summary>
    public bool AutoRefreshEnabled
    {
        get => _autoRefreshEnabled;
        set
        {
            if (_autoRefreshEnabled == value) return;
            _autoRefreshEnabled = value;
            Arm();

            var state = UiStateStore.Load();
            UiStateStore.Save(state with { AutoRefresh = value });
        }
    }

    /// <summary>查询指定城市。名称留空会被当成"没输入"处理，不会发请求。</summary>
    public async Task QueryAsync(string city)
    {
        // 按钮在查询期间是禁用的，这里再兜一道，防止 F5、启动参数、定时器重入
        if (_busy) return;

        var trimmed = city.Trim();
        if (trimmed.Length == 0)
        {
            LastStatus = "请先输入要查询的地点。";
            LastStatusIsError = true;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _busy = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var stopwatch = Stopwatch.StartNew();
        LastStatus = $"正在联网查询「{trimmed}」…";
        LastStatusIsError = false;
        Arm();                  // 查询期间把定时器停掉，查完在 finally 里按需重启
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            var report = await _service.GetWeatherAsync(trimmed, token);

            LastReport = report;
            _lastCity = trimmed;
            History = HistoryStore.Add(trimmed);

            stopwatch.Stop();
            LastElapsedMs = stopwatch.ElapsedMilliseconds;
            LastStatus = $"已更新「{report.Place.DisplayName}」的天气。";
        }
        catch (OperationCanceledException)
        {
            LastStatus = "已取消查询。";
        }
        catch (WeatherException error)
        {
            // Message 本来就是可以直接显示的中文文案
            LastStatus = error.Message;
            LastStatusIsError = true;
        }
        catch (Exception error)
        {
            LastStatus = "发生未知错误：" + error.Message;
            LastStatusIsError = true;
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            Arm();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>重查上次查询过的城市；还没查过就用历史里最新的一条。没有城市则什么都不做。</summary>
    public void RefreshLast()
    {
        var city = _lastCity ?? History.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(city)) _ = QueryAsync(city);
    }

    /// <summary>勾了自动刷新、且手里已经有成功数据时才转；否则停着等下一次成功。</summary>
    private void Arm()
    {
        if (_autoRefreshEnabled && LastReport is not null && !_busy)
            _timer.Start();
        else
            _timer.Stop();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
