using System.Runtime.InteropServices;
using WeatherHelper.Models;
using WeatherHelper.Services;

namespace WeatherHelper.Ui;

/// <summary>
/// 桌宠：只显示「图标 + 温度 + 城市」的一小块，贴在桌面上，背景全透明。
/// 左键按住文字可以拖着走，右键出菜单，悬停出完整字段。
/// 其余内容（体感、湿度、风、降水量、更新时间、数据源）都在悬停提示里，一个字段都不少。
///
/// 透明靠 WinForms 的键控透明（<see cref="Form.TransparencyKey"/>）：窗体底色和透明色都设成
/// 白色，于是除文字和图标以外的像素在系统层面就是「洞」——能看到壁纸，
/// 鼠标也会直接穿过去点到下面的窗口。也就是说<b>只有文字本身能拖、能右键、能悬停</b>。
/// 这是「绝对全透明」的代价，取舍见计划文档；真嫌难抓，把 <see cref="Key"/> 从纯白改成
/// <c>RGB(254,254,254)</c> 就能让整块区域都可交互，而肉眼几乎看不出那层底板。
/// </summary>
internal sealed class PetForm : Form
{
    /// <summary>
    /// 既是窗体底色也是透明色，所以最后只有文字像素留下。
    ///
    /// 这里用白而不是黑：桌宠的文字是黑的，透明色一旦也用黑，黑色字形本身的像素会被当成
    /// 透明色整块挖掉 —— 文字会直接消失。反过来用白，抗锯齿边缘会自然混出一圈淡淡的白描边。
    /// </summary>
    private static readonly Color Key = Color.White;

    /// <summary>天气符号沿用主窗口「实时天气」那个深蓝，和弹窗里看着是一套。</summary>
    private static readonly Color IconColor = Color.FromArgb(70, 110, 160);

    /// <summary>文字用纯黑。</summary>
    private static readonly Color TextColor = Color.Black;

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly WeatherHub _hub;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolTip _tip = new()
    {
        ShowAlways = true,      // 窗口没被激活时也要能弹出来
        AutoPopDelay = 15000,
        InitialDelay = 350,
        ReshowDelay = 150,
    };

    private readonly Label _lblIcon = new();
    private readonly Label _lblTemp = new();
    private readonly Label _lblPlace = new();

    /// <summary>桌宠没有关闭按钮，但 Alt+F4 能碰到它。只有「退出程序」才是真关。</summary>
    private bool _reallyClosing;

    /// <summary>WndProc 里转发 WM_CLOSE 时的重入标志。</summary>
    private bool _routingClose;

    /// <summary>右键菜单里的「打开主窗口」。</summary>
    public event EventHandler? OpenMainWindow;

    /// <summary>右键菜单里的「退出程序」。</summary>
    public event EventHandler? ExitRequested;

    public PetForm(WeatherHub hub)
    {
        _hub = hub;

        Text = "天气桌宠";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoSize = true;                        // 窗体缩到内容大小；反正透明区域看不见
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Key;
        TransparencyKey = Key;                  // WinForms 会顺带补上 WS_EX_LAYERED

        BuildLayout();
        BuildMenu();

        _hub.Changed += OnHubChanged;
    }

    private void OnHubChanged(object? sender, EventArgs e) => RenderFromHub();

    /// <summary>「退出程序」走这条路才是真关；否则 OnFormClosing 会把它拦成「隐藏」。</summary>
    internal void CloseForExit()
    {
        _reallyClosing = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 和主窗口一个道理：关掉不等于退出。真让它关掉的话，托盘的「显示桌宠」
        // 就只能对着一个已销毁的窗体 Show()，那才是真的会炸。
        if (!_reallyClosing && !CloseRules.IsForced(e.CloseReason))
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// 别的进程直接发来的 WM_CLOSE 绕开 <see cref="OnFormClosing"/> 把窗口直接销毁，
    /// 句柄没了托盘就再也唤不回它。转成 <see cref="Form.Close"/> 走正常流程。
    /// 重入标志是必须的：<c>Close()</c> 自己也是发 WM_CLOSE。
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == CloseRules.WmClose && !_routingClose)
        {
            _routingClose = true;
            try { Close(); }
            finally { _routingClose = false; }
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 退订：程序退出时可能还有一次查询的续体排在消息队列里，别让它去碰已销毁的控件
        _hub.Changed -= OnHubChanged;
        base.OnFormClosed(e);
    }

    /// <summary>不进 Alt+Tab、不进任务栏 —— 它是个挂件，不是窗口。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOOLWINDOW;
            return parameters;
        }
    }

    // ────────────────────────────── 布局 ──────────────────────────────

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(2),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _lblIcon.AutoSize = true;
        _lblIcon.BackColor = Color.Transparent;
        _lblIcon.Font = UiFonts.Icon(34f);
        _lblIcon.ForeColor = IconColor;
        _lblIcon.Margin = new Padding(0, 0, 8, 0);
        _lblIcon.Text = Format.Dash;

        var info = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        info.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        info.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblTemp.AutoSize = true;
        _lblTemp.BackColor = Color.Transparent;
        _lblTemp.Font = UiFonts.Ui(22f, FontStyle.Bold);
        _lblTemp.ForeColor = TextColor;
        _lblTemp.Margin = new Padding(0);
        _lblTemp.Text = Format.Dash;

        _lblPlace.AutoSize = true;
        _lblPlace.BackColor = Color.Transparent;
        _lblPlace.Font = UiFonts.Ui(9.5f);
        _lblPlace.ForeColor = TextColor;
        _lblPlace.Margin = new Padding(0);
        _lblPlace.Text = "尚未查询";

        info.Controls.Add(_lblTemp, 0, 0);
        info.Controls.Add(_lblPlace, 0, 1);

        root.Controls.Add(_lblIcon, 0, 0);
        root.Controls.Add(info, 1, 0);

        Controls.Add(root);

        // 子控件会自己吃掉鼠标消息，所以拖动的处理必须逐个挂到 Label 上，
        // 否则按住文字毫无反应 —— 而文字恰恰是唯一能命中的地方。
        foreach (var label in new[] { _lblIcon, _lblTemp, _lblPlace })
        {
            label.MouseDown += (_, e) => BeginDrag(e);
            label.ContextMenuStrip = _menu;     // 显式挂上，不依赖 WinForms 向上找父控件
        }
    }

    private void BuildMenu()
    {
        _menu.Items.Add(new ToolStripMenuItem("打开主窗口", null, (_, _) => OpenMainWindow?.Invoke(this, EventArgs.Empty)));
        _menu.Items.Add(new ToolStripMenuItem("刷新天气", null, (_, _) => _hub.RefreshLast()));
        _menu.Items.Add(new ToolStripMenuItem("隐藏到托盘", null, (_, _) => Hide()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出程序", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        ContextMenuStrip = _menu;
    }

    // ────────────────────────────── 渲染 ──────────────────────────────

    private void RenderFromHub()
    {
        if (_hub.IsBusy)
        {
            _lblTemp.Text = "…";
        }
        else if (_hub.LastReport is { } report)
        {
            _lblIcon.Text = report.Current.Icon;
            _lblTemp.Text = Format.Temp(report.Current.TemperatureC);
            _lblPlace.Text = report.Place.DisplayName;
        }
        else
        {
            _lblIcon.Text = Format.Dash;
            _lblTemp.Text = Format.Dash;
            _lblPlace.Text = "尚未查询";
        }

        var tip = BuildTipText();
        foreach (var control in new Control[] { this, _lblIcon, _lblTemp, _lblPlace })
            _tip.SetToolTip(control, tip);
    }

    /// <summary>悬停提示：桌宠上省掉的那些字段在这里补齐。</summary>
    private string BuildTipText()
    {
        if (_hub.LastReport is not { } report)
        {
            return _hub.LastStatusIsError
                ? _hub.LastStatus
                : "还没有查询结果。\n右键打开主窗口，输入城市后点「查询」。";
        }

        var current = report.Current;
        return string.Join('\n',
            report.Place.DisplayName,
            $"{current.Icon} {current.Description} · 体感 {Format.Temp(current.ApparentC)}",
            Format.Details(current),
            Format.Updated(report),
            $"数据来源：{report.Source}");
    }

    // ────────────────────────────── 位置 ──────────────────────────────

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RestorePosition();
    }

    /// <summary>按上次记下的位置摆放；没有记录就落到右下角。</summary>
    private void RestorePosition()
    {
        var state = UiStateStore.Load();

        var target = state.PetX is { } x && state.PetY is { } y
            ? new Point(x, y)
            : DefaultPosition();

        Location = ClampToScreen(target);
    }

    private Point DefaultPosition()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        return new Point(area.Right - Width - 24, area.Bottom - Height - 24);
    }

    /// <summary>夹进某块屏幕的工作区 —— 换过显示器之后，桌宠不至于被丢在看不见的地方。</summary>
    private static Point ClampToScreen(Point point)
    {
        var area = Screen.AllScreens.FirstOrDefault(screen => screen.WorkingArea.Contains(point))?.WorkingArea
                   ?? Screen.PrimaryScreen!.WorkingArea;

        // 至少留 40px 在屏幕里，保证还抓得住
        var x = Math.Clamp(point.X, area.Left, Math.Max(area.Left, area.Right - 40));
        var y = Math.Clamp(point.Y, area.Top, Math.Max(area.Top, area.Bottom - 40));
        return new Point(x, y);
    }

    private void SavePosition()
    {
        var state = UiStateStore.Load();
        UiStateStore.Save(state with { PetX = Left, PetY = Top });
    }

    // ────────────────────────────── 拖动 / 右键 ──────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        BeginDrag(e);
    }

    /// <summary>
    /// 借用系统的窗口移动循环来拖，不自己算鼠标增量，所以不会抖。
    /// <c>WM_NCLBUTTONDOWN</c> 这一句要等松手（移动循环结束）才返回，正好用来落盘。
    /// </summary>
    private void BeginDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;   // 右键交给 ContextMenuStrip

        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        SavePosition();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}