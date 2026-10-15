using System.Globalization;

namespace WeatherHelper.Models;

/// <summary>
/// 界面与命令行共用的显示格式化。缺失数据一律显示「—」而不是 0 或空白。
/// 数字全部用 <see cref="CultureInfo.InvariantCulture"/>，避免受系统区域设置影响。
/// </summary>
public static class Format
{
    public const string Dash = "—";

    public static string Temp(double? celsius)
        => celsius is null || double.IsNaN(celsius.Value)
            ? Dash
            : $"{celsius.Value.ToString("0.#", CultureInfo.InvariantCulture)} °C";

    public static string Percent(int? value)
        => value is null ? Dash : $"{value.Value.ToString(CultureInfo.InvariantCulture)}%";

    public static string Millimeters(double? value)
        => value is null ? Dash : $"{value.Value.ToString("0.#", CultureInfo.InvariantCulture)} mm";

    public static string Speed(double? kmh)
        => kmh is null ? Dash : $"{kmh.Value.ToString("0.#", CultureInfo.InvariantCulture)} km/h";

    public static string Clock(DateTime? value)
        => value is null ? Dash : value.Value.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Day(DateOnly value) => value.ToString("MM-dd", CultureInfo.InvariantCulture);

    /// <summary>把 <c>2026-10-14</c> 转成「10-14 周三」。</summary>
    public static string DayWithWeekday(DateOnly value)
    {
        var weekday = value.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日",
        };
        return $"{value.ToString("MM-dd", CultureInfo.InvariantCulture)} {weekday}";
    }

    /// <summary>把 <c>2026-10-14T06:23</c> 截成「06:23」；解析不了就原样返回。</summary>
    public static string? ShortClock(string? isoLocalTime)
    {
        if (string.IsNullOrWhiteSpace(isoLocalTime)) return null;
        return DateTime.TryParse(isoLocalTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("HH:mm", CultureInfo.InvariantCulture)
            : isoLocalTime;
    }
}