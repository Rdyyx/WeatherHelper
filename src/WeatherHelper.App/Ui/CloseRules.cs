namespace WeatherHelper.Ui;

/// <summary>
/// 「关掉不等于退出」这条规矩的共用部分：桌宠和主窗口都要求点 × / Alt+F4 只是把它们收起来。
/// </summary>
internal static class CloseRules
{
    public const int WmClose = 0x0010;

    /// <summary>
    /// 系统强制的关闭（关机、任务管理器结束进程）。这种不能拦 —— 拦了会卡住关机。
    /// </summary>
    public static bool IsForced(CloseReason reason)
        => reason is CloseReason.WindowsShutDown
                  or CloseReason.TaskManagerClosing
                  or CloseReason.ApplicationExitCall;
}