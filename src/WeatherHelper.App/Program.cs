using WeatherHelper.Services;
using WeatherHelper.Ui;

namespace WeatherHelper;

internal static class Program
{
    /// <summary>单实例判重。Local\ 前缀让它只在当前登录会话里生效，多用户互不干扰。</summary>
    private const string MutexName = @"Local\WeatherHelper.SingleInstance";

    /// <summary>第二个实例用它把已有实例的主窗口敲出来。</summary>
    private const string ShowSignalName = @"Local\WeatherHelper.ShowMainWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        // 由 SDK 生成：设置 HighDpiMode(PerMonitorV2) / 视觉样式 / 文本渲染方式
        ApplicationConfiguration.Initialize();

        // 必须在 Application.Run 之前设置，否则已经创建的窗口不会继承
        Application.SetDefaultFont(UiFonts.Ui(9f));

        // 全局异常兜底：漏网的异常弹中文提示，而不是 .NET 那个原始崩溃对话框
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();

        // 后台预热连接：首次请求的 DNS + TLS + 代理探测开销约 2–3 秒，
        // 放在启动时消化掉，用户真正点「查询」时就是热的。失败不影响功能。
        _ = HttpJson.WarmUpAsync();

        // 先建事件、再用 Mutex 判重。反过来的话，第二个实例可能在第一个实例还没建好事件时就去敲，白敲。
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);

        if (!isFirst)
        {
            // 已经有一个在跑：把它唤出来，自己安静退掉，不再开第二个桌宠
            try { showSignal.Set(); } catch (ObjectDisposedException) { }
            return;
        }

        // 支持可选的地名参数，便于不点鼠标就验证界面：
        //   WeatherHelper.exe 北京
        var startupCity = args.FirstOrDefault(arg => !arg.StartsWith('-'));

        // 自检用：启动时顺手把主窗口也打开（桌宠照常出现）
        var openDialog = args.Any(arg => arg.Equals("--dialog", StringComparison.OrdinalIgnoreCase));

        // using 兜底：Application.Run(context) 不保证会替我们 Dispose 这个 context
        using var controller = new AppController(showSignal, startupCity, openDialog);
        Application.Run(controller);
    }

    private static void ShowFatal(Exception? error)
    {
        MessageBox.Show(
            "程序遇到未预期的错误：\n\n" + (error?.Message ?? "未知错误"),
            "天气查询助手",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}