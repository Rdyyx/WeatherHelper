namespace WeatherHelper.Ui;

/// <summary>
/// 程序图标。同一份 app.ico 在 csproj 里挂了两个地方：
/// <c>ApplicationIcon</c> 把它写进 exe 的 Win32 图标资源（资源管理器、快捷方式里看到的），
/// <c>EmbeddedResource</c> 把它嵌进程序集 —— 这里读的是后者。
/// 不走"读 exe 旁边的 app.ico"：单文件自包含发布不会把它释放出来，按路径读会扑空。
/// </summary>
internal static class AppIcon
{
    /// <summary>就是 csproj 里那条 EmbeddedResource 的 LogicalName，改名要两边一起改。</summary>
    private const string ResourceName = "WeatherHelper.app.ico";

    private static bool _loaded;
    private static Icon? _cached;

    /// <summary>
    /// 窗口标题栏 / 任务栏 / Alt+Tab 用的图标。取不到就返回 null，让 Windows 用它自己的
    /// 默认图标 —— 为了一个图标把启动搞崩，不划算。
    /// </summary>
    public static Icon? Value
    {
        get
        {
            if (_loaded) return _cached;
            _loaded = true;

            try
            {
                using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
                if (stream is null) return null;

                // 取 32 那一档：WinForms 会把同一个句柄同时塞给 ICON_SMALL 和 ICON_BIG，
                // 拿 SmallIconSize(16) 去充当 Alt+Tab 的图标会糊，32 缩到 16 则没问题。
                _cached = new Icon(stream, SystemInformation.IconSize);
            }
            catch (Exception)
            {
                // 资源损坏、GDI+ 抽风……都只是没图标而已，静默降级
                _cached = null;
            }

            return _cached;
        }
    }
}
