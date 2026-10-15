using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using WeatherHelper.Models;
using WeatherHelper.Services;

namespace WeatherHelper.Ui;

/// <summary>
/// 主窗口。纯代码布局（不用 Designer）—— 全部控件都 Dock/Anchor 进
/// <see cref="TableLayoutPanel"/>，不写死像素坐标，这样 100%/125%/150% 缩放和
/// 窗口拉伸都不会把布局挤垮。
/// </summary>
internal sealed class MainForm : Form
{
    private readonly ComboBox _cmbCity = new();
    private readonly Button _btnQuery = new();
    private readonly CheckBox _chkAutoRefresh = new() { Text = "每 30 分钟自动刷新", AutoSize = true };
    private readonly Button _btnCopy = new() { Text = "复制天气简报", AutoSize = false };
    private readonly Label _lblStatus = new();
    private readonly Label _lblIcon = new();
    private readonly Label _lblPlace = new();
    private readonly Label _lblTemp = new();
    private readonly Label _lblDesc = new();
    private readonly Label _lblDetails = new();
    private readonly Label _lblUpdated = new();
    private readonly DataGridView _dgvDaily = NewGrid();
    private readonly DataGridView _dgvHourly = NewGrid();
    private readonly ToolStripStatusLabel _lblSource = new();
    private readonly ToolStripStatusLabel _lblElapsed = new();
    private readonly ToolStripProgressBar _pbBusy = new();

    private readonly WeatherHub _hub;
    private readonly string? _startupCity;

    /// <summary>点 × 只是把窗口收起来。只有「退出程序」才先把它置 true 再关。</summary>
    private bool _reallyClosing;

    /// <summary>WndProc 里转发 WM_CLOSE 时的重入标志。</summary>
    private bool _routingClose;

    public MainForm(WeatherHub hub, string? startupCity)
    {
        _hub = hub;
        _startupCity = startupCity;

        Text = "天气查询助手";
        // 标题栏/任务栏/Alt+Tab 用这张图；exe 文件本身那个由 csproj 的 ApplicationIcon 负责。
        Icon = AppIcon.Value;
        ClientSize = new Size(840, 680);
        MinimumSize = new Size(680, 540);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        KeyPreview = true;

        BuildLayout();

        // 联网、定时器、当前数据都归 hub，这个窗口只是它的一个视图
        _hub.Changed += OnHubChanged;

        AcceptButton = _btnQuery;
        _cmbCity.Focus();
    }

    // ────────────────────────────── 布局 ──────────────────────────────

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14, 12, 14, 8),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 搜索行
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 状态行
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 实时天气
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 预报页签

        root.Controls.Add(BuildSearchRow(), 0, 0);
        root.Controls.Add(BuildStatusLabel(), 0, 1);
        root.Controls.Add(BuildCurrentGroup(), 0, 2);
        root.Controls.Add(BuildTabs(), 0, 3);

        // 顺序很重要：Fill 的先加，Bottom 的后加，否则状态栏会被盖住
        Controls.Add(root);
        Controls.Add(BuildStatusStrip());
    }

    private Control BuildSearchRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 8),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _cmbCity.Dock = DockStyle.Fill;
        _cmbCity.Font = UiFonts.Ui(11f);
        _cmbCity.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _cmbCity.AutoCompleteSource = AutoCompleteSource.ListItems;
        _cmbCity.Margin = new Padding(0, 0, 8, 0);

        var historyMenu = new ContextMenuStrip();
        var clearItem = new ToolStripMenuItem("清空历史记录");
        clearItem.Click += (_, _) =>
        {
            HistoryStore.Clear();
            ApplyHistory([]);
        };
        historyMenu.Items.Add(clearItem);
        _cmbCity.ContextMenuStrip = historyMenu;

        _btnQuery.Text = "查询";
        _btnQuery.AutoSize = false;
        _btnQuery.Size = new Size(104, 34);
        _btnQuery.Font = UiFonts.Ui(11f);
        _btnQuery.Margin = new Padding(0, 0, 8, 0);
        _btnQuery.Click += BtnQuery_Click;

        _btnCopy.AccessibleName = "复制天气简报";
        _btnCopy.Size = new Size(120, 34);
        _btnCopy.Font = UiFonts.Ui(11f);
        _btnCopy.Margin = new Padding(0);
        _btnCopy.Enabled = false;
        _btnCopy.Click += BtnCopy_Click;

        row.Controls.Add(_cmbCity, 0, 0);
        row.Controls.Add(_btnQuery, 1, 0);
        row.Controls.Add(_btnCopy, 2, 0);
        return row;
    }

    private Control BuildStatusLabel()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 8),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _chkAutoRefresh.Font = UiFonts.Ui(9.5f);
        _chkAutoRefresh.Margin = new Padding(0, 0, 12, 0);
        _chkAutoRefresh.CheckedChanged += ChkAutoRefresh_CheckedChanged;

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.AutoEllipsis = true;
        _lblStatus.Font = UiFonts.Ui(9.5f);
        _lblStatus.ForeColor = Color.DimGray;
        _lblStatus.Margin = new Padding(0, 0, 0, 0);
        _lblStatus.Text = "就绪。输入地名后点击「查询」。";

        row.Controls.Add(_chkAutoRefresh, 0, 0);
        row.Controls.Add(_lblStatus, 1, 0);
        return row;
    }

    private Control BuildCurrentGroup()
    {
        var group = new GroupBox
        {
            Text = "实时天气",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = UiFonts.Ui(10f),
            Padding = new Padding(12, 8, 12, 10),
            Margin = new Padding(0, 0, 0, 10),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _lblIcon.Dock = DockStyle.Fill;
        _lblIcon.TextAlign = ContentAlignment.MiddleCenter;
        _lblIcon.Font = UiFonts.Icon(44f);
        _lblIcon.ForeColor = Color.FromArgb(70, 110, 160);
        _lblIcon.Text = "—";

        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        for (var i = 0; i < 5; i++) info.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _lblPlace.AutoSize = true;
        _lblPlace.Font = UiFonts.Ui(12f, FontStyle.Bold);
        _lblPlace.Text = "尚未查询";

        _lblTemp.AutoSize = true;
        _lblTemp.Font = UiFonts.Ui(28f, FontStyle.Bold);
        _lblTemp.ForeColor = Color.FromArgb(40, 80, 140);
        _lblTemp.Text = Format.Dash;

        _lblDesc.AutoSize = true;
        _lblDesc.Font = UiFonts.Ui(11.5f);
        _lblDesc.Text = Format.Dash;

        _lblDetails.AutoSize = true;
        _lblDetails.Font = UiFonts.Ui(10f);
        _lblDetails.ForeColor = Color.DimGray;
        _lblDetails.Text = Format.Dash;

        _lblUpdated.AutoSize = true;
        _lblUpdated.Font = UiFonts.Ui(9f);
        _lblUpdated.ForeColor = Color.Gray;
        _lblUpdated.Text = string.Empty;

        info.Controls.Add(_lblPlace, 0, 0);
        info.Controls.Add(_lblTemp, 0, 1);
        info.Controls.Add(_lblDesc, 0, 2);
        info.Controls.Add(_lblDetails, 0, 3);
        info.Controls.Add(_lblUpdated, 0, 4);

        layout.Controls.Add(_lblIcon, 0, 0);
        layout.Controls.Add(info, 1, 0);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = UiFonts.Ui(10f),
            Margin = new Padding(0),
        };

        var dailyPage = new TabPage("未来 7 天") { Padding = new Padding(6), BackColor = Color.White };
        var hourlyPage = new TabPage("未来 24 小时") { Padding = new Padding(6), BackColor = Color.White };

        ConfigureGrid(_dgvDaily);
        AddColumn(_dgvDaily, "日期", 90);
        AddColumn(_dgvDaily, "天气", 150);
        AddColumn(_dgvDaily, "最高", 80);
        AddColumn(_dgvDaily, "最低", 80);
        AddColumn(_dgvDaily, "降水概率", 90);
        AddColumn(_dgvDaily, "日出", 70);
        AddColumn(_dgvDaily, "日落", 70);

        ConfigureGrid(_dgvHourly);
        AddColumn(_dgvHourly, "时间", 90);
        AddColumn(_dgvHourly, "天气", 200);
        AddColumn(_dgvHourly, "温度", 110);
        AddColumn(_dgvHourly, "降水概率", 110);

        dailyPage.Controls.Add(_dgvDaily);
        hourlyPage.Controls.Add(_dgvHourly);
        tabs.TabPages.Add(dailyPage);
        tabs.TabPages.Add(hourlyPage);
        return tabs;
    }

    private Control BuildStatusStrip()
    {
        var strip = new StatusStrip { SizingGrip = false, Font = UiFonts.Ui(9f) };

        _lblSource.Spring = true;
        _lblSource.TextAlign = ContentAlignment.MiddleLeft;
        _lblSource.Text = $"降级顺序：{_hub.SourceName}";

        _pbBusy.Style = ProgressBarStyle.Marquee;
        _pbBusy.MarqueeAnimationSpeed = 30;
        _pbBusy.Visible = false;
        _pbBusy.Width = 120;

        _lblElapsed.Text = string.Empty;

        strip.Items.Add(_lblSource);
        strip.Items.Add(_pbBusy);
        strip.Items.Add(_lblElapsed);
        return strip;
    }

    /// <summary>
    /// 建一个只读、整行选中、不带行头的表格。
    /// DataGridView 不像其它控件那样自动继承默认字体，字体得在 <see cref="ConfigureGrid"/> 里显式设置，
    /// 否则 DPI 缩放后行高会塌。
    /// </summary>
    private static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        ReadOnly = true,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        BorderStyle = BorderStyle.None,
        BackgroundColor = Color.White,
        EnableHeadersVisualStyles = false,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
        GridColor = Color.FromArgb(230, 230, 230),
    };

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowTemplate.Height = 30;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 251);
        grid.DefaultCellStyle.Font = UiFonts.Ui(10f);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 250);
        grid.DefaultCellStyle.SelectionForeColor = Color.Black;
        grid.ColumnHeadersDefaultCellStyle.Font = UiFonts.Ui(10f, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 241, 245);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 60, 60);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
    }

    private static void AddColumn(DataGridView grid, string header, int fillWeight)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            FillWeight = fillWeight,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        });
    }

    // ────────────────────────────── 交互 ──────────────────────────────

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        SetCueBanner(_cmbCity, "输入地点，例如：北京 / 上海 / 深圳 / Tokyo");
        ApplyHistory(_hub.History);

        // 记住上次查询的城市：历史里最新一条（第 0 条）预填进输入框，方便直接回车查询。
        // 只在框里空着时填 —— 收起来再打开不该把用户刚敲进去的地名冲掉。
        if (string.IsNullOrWhiteSpace(_startupCity) && _cmbCity.Text.Trim().Length == 0 && _hub.History.Count > 0)
            _cmbCity.Text = _hub.History[0];

        if (!string.IsNullOrWhiteSpace(_startupCity))
            _cmbCity.Text = _startupCity;

        // 勾选框反映 hub 的真实状态（桌宠常驻，默认就是开着的）。
        // 启动时那次查询由 hub 负责，这里不再自己查一遍。
        _chkAutoRefresh.Checked = _hub.AutoRefreshEnabled;

        RenderFromHub();
    }

    private void OnHubChanged(object? sender, EventArgs e) => RenderFromHub();

    /// <summary>「退出程序」走这条路才是真关；否则 OnFormClosing 会把它拦成"隐藏"。</summary>
    internal void CloseForExit()
    {
        _reallyClosing = true;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // 退订：程序退出时可能还有一次查询的续体排在消息队列里，别让它去碰已销毁的控件
        _hub.Changed -= OnHubChanged;
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 关掉不等于退出：把窗口收起来，桌宠继续在桌面上跑。
        // 只有系统强制的关闭（关机、任务管理器结束进程）不能拦，否则会卡住关机。
        if (!_reallyClosing && !CloseRules.IsForced(e.CloseReason))
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// 别的进程直接发来的 WM_CLOSE（任务管理器之类）不会走 <see cref="OnFormClosing"/>，
    /// 而是把窗口直接销毁掉 —— 句柄一旦没了，之后再 Show() 只能整条重建。
    /// 这里把它转成 <see cref="Form.Close"/>，让它老老实实走正常流程（→ OnFormClosing → 隐藏）。
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            BtnQuery_Click(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async void BtnQuery_Click(object? sender, EventArgs e)
    {
        // 重入保护、取消、错误分类都在 WeatherHub 里，这里只负责把输入框的地名递过去
        if (_hub.IsBusy) return;

        if (_cmbCity.Text.Trim().Length == 0)
        {
            await _hub.QueryAsync(string.Empty);   // 让 hub 统一报「请先输入要查询的地点。」
            _cmbCity.Focus();
            return;
        }

        await _hub.QueryAsync(_cmbCity.Text);
    }

    /// <summary>
    /// 唯一的渲染入口。hub 一变就整体重绘，所以「自己点了一次查询」和「被 hub 通知了一次」
    /// 不会各画一遍 —— 那本来就是同一次变化。
    /// </summary>
    private void RenderFromHub()
    {
        SetBusy(_hub.IsBusy, _hub.LastStatus, _hub.LastStatusIsError);
        _lblElapsed.Text = _hub.LastElapsedMs > 0 ? $"耗时 {_hub.LastElapsedMs} ms" : string.Empty;

        if (_hub.LastReport is { } report) RenderReport(report);

        ApplyHistory(_hub.History);
    }

    private void SetBusy(bool busy, string status, bool isError = false)
    {
        _btnQuery.Enabled = !busy;      // 禁用按钮，防止重复查询
        _cmbCity.Enabled = !busy;
        Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        _pbBusy.Visible = busy;

        _lblStatus.Text = status;
        _lblStatus.ForeColor = isError ? Color.Firebrick : busy ? Color.SteelBlue : Color.DimGray;
    }

    private void RenderReport(WeatherReport report)
    {
        var current = report.Current;

        _lblPlace.Text = report.Place.DisplayName;
        _lblIcon.Text = current.Icon;
        _lblTemp.Text = Format.Temp(current.TemperatureC);
        _lblDesc.Text = $"{current.Description} · 体感 {Format.Temp(current.ApparentC)}";
        _lblDetails.Text = Format.Details(current);
        _lblUpdated.Text = Format.Updated(report);

        _dgvDaily.Rows.Clear();
        foreach (var day in report.Days)
        {
            _dgvDaily.Rows.Add(
                Format.DayWithWeekday(day.Date),
                $"{day.Icon} {day.Description}",
                Format.Temp(day.MaxC),
                Format.Temp(day.MinC),
                Format.Percent(day.PrecipProbabilityMax),
                day.Sunrise ?? Format.Dash,
                day.Sunset ?? Format.Dash);
        }

        _dgvHourly.Rows.Clear();
        foreach (var hour in report.Hours)
        {
            _dgvHourly.Rows.Add(
                Format.Clock(hour.Time),
                $"{hour.Icon} {hour.Description}",
                Format.Temp(hour.TempC),
                Format.Percent(hour.PrecipProbability));
        }

        _lblSource.Text = $"数据来源：{report.Source}";

        // 有新数据才能复制简报。自动刷新归 hub 管，这里不用操心。
        _btnCopy.Enabled = true;
    }

    // ────────────────────────────── 自动刷新 / 复制简报 ──────────────────────────────

    private void ChkAutoRefresh_CheckedChanged(object? sender, EventArgs e)
        => _hub.AutoRefreshEnabled = _chkAutoRefresh.Checked;

    /// <summary>把当前天气整理成一段适合粘贴/转发的中文简报，复制到剪贴板。</summary>
    private void BtnCopy_Click(object? sender, EventArgs e)
    {
        if (_hub.LastReport is not { } report) return;

        var current = report.Current;
        var builder = new StringBuilder();

        builder.Append("【天气简报】");
        builder.AppendLine(report.Place.DisplayName);
        builder.AppendLine($"{current.Icon} {current.Description}");
        builder.AppendLine($"温度 {Format.Temp(current.TemperatureC)}（体感 {Format.Temp(current.ApparentC)}）");
        builder.Append($"湿度 {Format.Percent(current.Humidity)}    风 {current.WindText}");
        if (current.PrecipitationMm is not null)
            builder.Append($"    降水量 {Format.Millimeters(current.PrecipitationMm)}");
        builder.AppendLine();
        builder.AppendLine();

        if (report.Days.Count > 0)
        {
            builder.AppendLine("未来几天：");
            foreach (var day in report.Days)
            {
                builder.AppendLine($"  {Format.DayWithWeekday(day.Date)}  {day.Icon} {day.Description}  "
                    + $"{Format.Temp(day.MaxC)} / {Format.Temp(day.MinC)}");
            }
            builder.AppendLine();
        }

        builder.Append("数据来源 ").Append(report.Source).Append(" · 更新于 ")
            .Append(report.FetchedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

        try
        {
            Clipboard.SetText(builder.ToString());
            SetBusy(false, "已复制天气简报到剪贴板，可直接粘贴发送。");
        }
        catch (Exception ex)
        {
            SetBusy(false, "复制失败：" + ex.Message, isError: true);
        }
    }

    /// <summary>重建下拉项的同时保留输入框里已有的文字。</summary>
    private void ApplyHistory(IReadOnlyList<string> history)
    {
        var text = _cmbCity.Text;

        _cmbCity.BeginUpdate();
        _cmbCity.Items.Clear();
        foreach (var city in history) _cmbCity.Items.Add(city);
        _cmbCity.EndUpdate();

        _cmbCity.Text = text;
    }

    private const int CbSetCueBanner = 0x1703;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    /// <summary>
    /// 给下拉框加占位提示。<c>PlaceholderText</c> 只有 TextBox 才有，
    /// ComboBox 得靠 Win32 的 <c>CB_SETCUEBANNER</c> 消息。
    /// </summary>
    private static void SetCueBanner(ComboBox combo, string text)
    {
        if (combo.IsHandleCreated)
            SendMessage(combo.Handle, CbSetCueBanner, IntPtr.Zero, text);
    }
}