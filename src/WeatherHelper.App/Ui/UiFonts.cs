using System.Drawing.Text;

namespace WeatherHelper.Ui;

/// <summary>
/// 字体探测。不写死字体名 —— 目标机器上未必装了雅黑，硬写会退化成难看的默认字体。
/// 这里按优先级挑第一个真实装了的。
/// </summary>
internal static class UiFonts
{
    /// <summary>界面上选第一个装了的：雅黑 UI → 雅黑 → 宋体 → 微软正黑。</summary>
    private static readonly string UiFamily = PickFirst(
        "Microsoft YaHei UI",
        "Microsoft YaHei",
        "SimSun",
        "Microsoft JhengHei UI");

    /// <summary>
    /// 天气符号字体。用 Segoe UI Symbol 而不是 Segoe UI Emoji ——
    /// WinForms 的 GDI+ 不支持 COLR/CPAL 彩色字体，彩色 emoji 会渲染成单色或豆腐块。
    /// </summary>
    private static readonly string IconFamily = PickFirst(
        "Segoe UI Symbol",
        "Segoe UI Emoji",
        "Microsoft YaHei UI");

    public static Font Ui(float size) => new(UiFamily, size);

    public static Font Ui(float size, FontStyle style) => new(UiFamily, size, style);

    public static Font Icon(float size) => new(IconFamily, size);

    private static string PickFirst(params string[] candidates)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var collection = new InstalledFontCollection();
            foreach (var family in collection.Families) installed.Add(family.Name);
        }
        catch (Exception)
        {
            // 拿不到字体列表就退到系统默认
        }

        foreach (var candidate in candidates)
        {
            if (installed.Contains(candidate)) return candidate;
        }

        return SystemFonts.MessageBoxFont?.FontFamily.Name ?? FontFamily.GenericSansSerif.Name;
    }
}