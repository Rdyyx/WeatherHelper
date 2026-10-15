# WeatherHelper — 天气查询助手

## 项目概述

一个 Windows 桌面天气查询助手（WinForms, .NET 8），每次查询实时联网获取指定地点的天气。主数据源免费，无需 API Key。

进程模型是「一个桌宠 + 一个弹窗」：

- 双击 exe 出现的是 **桌宠** —— 只有天气图标、温度、城市名的一小块，背景全透明、始终置顶，
  贴在桌面上当挂件。左键按住文字能拖走，悬停看完整字段，右键出菜单。
- 右键桌宠「打开主窗口」才弹出完整窗口（就是原来的 `MainForm`）：改城市、看 7 天/24 小时预报、
  复制简报都在这里。**在弹窗里改的城市，关掉弹窗后桌宠会跟着变** —— 两者读的是同一份数据。
- 关掉弹窗只是隐藏，程序继续跑（桌宠还在）；真正的退出在桌宠/托盘的右键菜单里。

**核心能力：**
- 实时天气（温度、体感温度、湿度、风向/风速、降水量）
- 未来 7 天日报 + 24 小时逐时预报
- 桌宠常驻桌面：全透明背景、始终置顶、可拖动、位置记忆、悬停看详情
- 托盘图标跟着当前天气变（运行时画出来的，不依赖图片资源）
- 输入框自动补全历史记录
- 记住上次查询的城市（启动时自动预填）
- 自动刷新（默认开启，每 30 分钟重新查询）
- 一键复制天气简报
- 多数据源故障自动降级
- 单实例：重复双击 exe 只会把已有实例的主窗口唤到前台

## 项目结构

```
src/
├── WeatherHelper.Core/         # 核心库（无外部依赖，无 NuGet）
│   ├── Json/JsonHelp.cs        # JSON 安全读取工具
│   ├── Models/                 # 数据模型
│   │   ├── WeatherReport.cs    # 完整查询结果
│   │   ├── Location.cs         # 地点（含 DisplayName 中文拼接）
│   │   ├── CurrentWeather.cs   # 当前实况
│   │   ├── DailyForecast.cs    # 日报
│   │   ├── HourlyPoint.cs      # 逐时预报
│   │   ├── Format.cs           # 中文格式化（温度/湿度/时间等）
│   │   ├── WeatherCodes.cs     # WMO 天气码 → 中文描述 + 图标
│   │   └── WwoCodes.cs         # WWO 天气码 → 中文描述（wttr.in 用）
│   └── Services/
│       ├── IWeatherService.cs         # 数据源接口
│       ├── WeatherService.cs          # 编排层：和风→Open-Meteo→wttr.in 降级
│       ├── OpenMeteoService.cs        # 主数据源（免费，无需 Key）
│       ├── WttrInService.cs           # 备用源 wttr.in
│       ├── QWeatherService.cs         # 可选源：和风天气（需配置 Key）
│       ├── HttpJson.cs                # HTTP 客户端（自动重试、超时控制）
│       ├── AppSettings.cs             # settings.json 配置（只读）
│       ├── HistoryStore.cs            # 查询历史持久化（%APPDATA%）
│       ├── UiStateStore.cs            # 界面状态持久化：桌宠位置、自动刷新开关
│       └── WeatherException.cs        # 面向用户的中文异常
├── WeatherHelper.App/          # WinForms 桌面应用
│   ├── Ui/
│   │   ├── AppController.cs    # 总控（ApplicationContext）：托盘、懒加载主窗口、退出
│   │   ├── WeatherHub.cs       # 唯一的联网入口：数据源 + 30 分钟定时器 + 当前报告
│   │   ├── PetForm.cs          # 桌宠（透明背景、置顶、可拖、右键菜单、悬停详情）
│   │   ├── MainForm.cs         # 完整弹窗（纯代码布局，无 Designer）
│   │   ├── CloseRules.cs       # 「关掉不等于退出」这条规矩的共用部分
│   │   └── UiFonts.cs          # 字体探测（优先雅黑，自动降级）
│   └── Program.cs              # 入口：单实例判重 + ApplicationContext
├── WeatherHelper.Cli/          # CLI 自检/调试工具
│   └── Program.cs              # 查询 + JSON 原始数据转储
├── dist_probe/                 # 之前打包的单文件 exe 探针
└── .gitignore
```

**架构决策：**
- **一个 hub，两个视图**：联网和 30 分钟定时器都住在 `WeatherHub`，`PetForm` 和 `MainForm` 只是它的视图（订阅 `Changed` 后各自重绘）。所以「弹窗里改城市 → 桌宠跟着变」是自动成立的，不需要两个窗口互相传值；弹窗一直不打开也不影响桌宠刷新。
- **关掉不等于退出**：`Program` 用 `ApplicationContext`（不是 `Application.Run(窗体)`）来决定程序什么时候结束。两个窗体点 × / Alt+F4 都只是 `Hide()`；只有菜单里的「退出程序」才真关。系统强制关闭（关机、任务管理器）不拦。
- **桌宠的透明靠键控透明**：`TransparencyKey` 和窗体底色都设成白色 —— 白色像素在系统层面就是「洞」，于是只剩文字和图标可见，鼠标也会穿透空白处。**代价是空白处不能拖也不能右键，只能按住文字**（这是「绝对全透明」的必然结果）。注意文字颜色不能和透明色相同，否则字形本身会被整块挖掉。
- **零素材**：托盘图标和桌宠用的天气符号都是运行时画出来的，仓库里没有 `.ico` / `.png` / `.resx`。
- **零 NuGet 依赖**：WeatherHelper.Core 不引用任何 NuGet 包，整个构建无需联网还原。仅 WinForms App 项目引用 System.Windows.Forms（框架自带）。
- **纯代码布局**：MainForm 全部用 `TableLayoutPanel` + Dock/Anchor，不写死像素坐标，DPI 缩放自适应。
- **故障降级**：Open-Meteo（主）→ wttr.in（备用）→ 用户配置了 Key 时可优先用和风天气。
- **字体自动选择**：UiFonts 运行时探测已安装的字体，不硬编码字体名。
- **容错设计**：配置损坏、历史记录读写失败、HTTP 超时均静默降级，不炸窗口。

## 数据源

| 来源 | API Key | 免费额度 | 日报 | 逐时 |
|------|---------|----------|------|------|
| Open-Meteo | 不需要 | 无限制 | 7 天 | 24 条 |
| wttr.in (备用) | 不需要 | 有限制 | 3 天 | 8 条 |
| 和风天气 (可选) | 需要 | 每日限额 | 3 天 | 24 条 |

数据源通过 `settings.json`（在 exe 同级目录）配置和风天气 Key；开箱即用的情况下完全无需配置。

## 构建与发布

```powershell
# 构建（无需还原）
dotnet build src\WeatherHelper.App -c Release

# 发布单文件自包含 exe
dotnet publish src\WeatherHelper.App -c Release -r win-x64 --self-contained true

# 生成物在
# .\dist\WeatherHelper.exe
```

.sln 结构：`src` → `WeatherHelper.Core` → `WeatherHelper.App` / `WeatherHelper.Cli`

## 贡献约定

- 使用 `System.Text.Json`，不引入 Newtonsoft.Json
- 异常用 `WeatherException`（中文 Message），不抛裸 Exception
- 所有缺失数据统一显示 `—`，不用 0 或空白
- WinForms 控件用代码创建（不用 Designer .Designer.cs），便于 review
- 留言用中文，代码注释也用中文