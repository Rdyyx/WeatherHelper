using WeatherHelper.Services;
using WeatherHelper.Ui;

namespace WeatherHelper;

internal static class Program
{
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

        // 支持可选的地名参数，便于不点鼠标就验证界面：
        //   WeatherHelper.exe 北京
        var startupCity = args.FirstOrDefault(arg => !arg.StartsWith('-'));

        Application.Run(new MainForm(startupCity));
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