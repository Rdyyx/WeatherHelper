using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using WeatherHelper.Models;
using WeatherHelper.Services;

namespace WeatherHelper.Ui;

/// <summary>
/// 程序总控。用 <see cref="ApplicationContext"/> 而不是 <c>Application.Run(窗体)</c>：
/// 桌宠和主窗口都是「关掉即隐藏」，程序什么时候结束必须自己说了算。
///
/// 一个 <see cref="WeatherHub"/>、两个视图（桌宠 + 弹窗），再加一个托盘图标 ——
/// 三者的内容全部来自同一个 hub，所以不会互相对不上。
/// </summary>
internal sealed class AppController : ApplicationContext
{
    private const string AppName = "天气查询助手";

    /// <summary><see cref="NotifyIcon.Text"/> 超过 63 个字符会抛异常，得自己截。</summary>
    private const int MaxTrayTextLength = 63;

    private readonly WeatherHub _hub;
    private readonly PetForm _pet;
    private readonly NotifyIcon _tray = new();

    private MainForm? _main;        // 懒加载：右键「打开主窗口」才建
    private Icon? _trayIcon;
    private bool _exiting;

    public AppController(EventWaitHandle? showSignal, string? startupCity, bool openDialog)
    {
        _hub = new WeatherHub(UiStateStore.Load().AutoRefresh);

        _pet = new PetForm(_hub);
        _pet.OpenMainWindow += (_, _) => ShowMainWindow();
        _pet.ExitRequested += (_, _) => ExitApp();

        BuildTray();
        _hub.Changed += OnHubChanged;

        _pet.Show();
        RenderTray();

        if (showSignal is not null) WatchShowSignal(showSignal);
        if (openDialog) ShowMainWindow();

        // 首个城市：命令行给的最优先，否则用历史里最新查过的那个。
        // 必须 Post 进消息循环再查 —— 此刻 Application.Run 还没开始，
        // WinForms 的 SynchronizationContext 还没装上；直接 await 的话续体会落到
        // 线程池上，接着就要跨线程碰控件。
        var city = string.IsNullOrWhiteSpace(startupCity) ? _hub.History.FirstOrDefault() : startupCity;
        if (!string.IsNullOrWhiteSpace(city))
            _pet.BeginInvoke((Action)(() => _ = _hub.QueryAsync(city)));
    }

    private void OnHubChanged(object? sender, EventArgs e) => RenderTray();

    // ────────────────────────────── 窗口 ──────────────────────────────

    /// <summary>打开（或唤回）完整弹窗。桌宠右键、托盘双击、第二个实例敲事件都走这里。</summary>
    public void ShowMainWindow()
    {
        if (_exiting) return;

        if (_main is null) _main = new MainForm(_hub, startupCity: null);

        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;

        _main.Activate();
        _main.BringToFront();
    }

    /// <summary>桌宠「隐藏到托盘」之后的回家路。</summary>
    private void ShowPet()
    {
        _pet.Show();
        _pet.BringToFront();
    }

    /// <summary>真退出。和「点 × 只是隐藏」相对。</summary>
    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;

        _tray.Visible = false;

        // 关掉窗体时它们各自会退订 hub，所以之后再来 Changed 也不会碰到已销毁的控件
        _pet.CloseForExit();
        _main?.CloseForExit();

        ExitThread();
    }

    // ────────────────────────────── 托盘 ──────────────────────────────

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("显示桌宠", null, (_, _) => ShowPet()));
        menu.Items.Add(new ToolStripMenuItem("打开主窗口", null, (_, _) => ShowMainWindow()));
        menu.Items.Add(new ToolStripMenuItem("刷新天气", null, (_, _) => _hub.RefreshLast()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出程序", null, (_, _) => ExitApp()));

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMainWindow();

        // NotifyIcon 必须先有 Icon 才能置 Visible
        _trayIcon = CreateGlyphIcon(Format.Dash);
        _tray.Icon = _trayIcon;
        _tray.Text = AppName;
        _tray.Visible = true;
    }

    /// <summary>托盘图标就是桌宠上那个天气符号 —— 仓库里一张图片资源都没有，全靠现画。</summary>
    private void RenderTray()
    {
        if (_exiting) return;

        var previous = _trayIcon;
        _trayIcon = CreateGlyphIcon(_hub.LastReport?.Current.Icon ?? Format.Dash);
        _tray.Icon = _trayIcon;
        previous?.Dispose();

        _tray.Text = Truncate(_hub.LastReport is { } report
            ? $"{AppName} · {report.Place.DisplayName} {Format.Temp(report.Current.TemperatureC)}"
            : AppName);
    }

    /// <summary>
    /// 把天气符号画成托盘图标。必须 <c>Clone</c> 一份出来：
    /// <see cref="Icon.FromHandle"/> 借来的 Icon 并不拥有那个 GDI 句柄，得自己 <c>DestroyIcon</c>
    /// 还回去，否则每换一次图标就漏一个句柄。
    /// </summary>
    private static Icon CreateGlyphIcon(string glyph)
    {
        var size = SystemInformation.SmallIconSize;

        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var font = UiFonts.Icon(size.Height * 0.8f);
            using var brush = new SolidBrush(Color.FromArgb(70, 110, 160));   // 和主窗口的天气图标同色
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            graphics.DrawString(glyph, font, brush, new RectangleF(PointF.Empty, size), format);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static string Truncate(string text)
        => text.Length <= MaxTrayTextLength ? text : text[..MaxTrayTextLength];

    // ────────────────────────────── 单实例唤醒 ──────────────────────────────

    /// <summary>后台等第二个实例敲事件；敲到就把主窗口唤到前台。</summary>
    private void WatchShowSignal(EventWaitHandle signal)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (!signal.WaitOne()) return;
                    if (_exiting || !_pet.IsHandleCreated) return;

                    _pet.BeginInvoke((Action)ShowMainWindow);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;     // 窗体已经销毁，收工
                }
            }
        })
        {
            IsBackground = true,
            Name = "单实例唤醒",
        };

        thread.Start();
    }

    // ────────────────────────────── 收尾 ──────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _trayIcon?.Dispose();
            _hub.Dispose();
            _pet.Dispose();
            _main?.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}