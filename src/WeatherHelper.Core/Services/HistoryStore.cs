using System.Text;
using System.Text.Json;
using WeatherHelper.Json;

namespace WeatherHelper.Services;

/// <summary>
/// 查询历史，持久化在 <c>%APPDATA%\WeatherHelper\history.json</c>，最多保留 10 条。
/// 读写失败一律静默忽略 —— 历史记录丢了是小事，不能因此让查询报错。
/// </summary>
public static class HistoryStore
{
    private const int MaxEntries = 10;

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WeatherHelper");

    public static string FilePath => Path.Combine(DirectoryPath, "history.json");

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            using var document = JsonHelp.Parse(json);
            if (document is null) return [];

            var cities = document.RootElement.Arr("cities");
            if (cities is null) return [];

            var result = new List<string>();
            for (var i = 0; i < cities.Value.Len(); i++)
            {
                var city = cities.Value.StrAt(i);
                if (!string.IsNullOrWhiteSpace(city)) result.Add(city.Trim());
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>把城市置顶、去重、截断到 10 条，并持久化。</summary>
    public static IReadOnlyList<string> Add(string city)
    {
        var trimmed = city.Trim();
        if (trimmed.Length == 0) return Load();

        // 大小写不敏感去重，但保留用户这次输入的原样写法
        var result = new List<string> { trimmed };
        foreach (var existing in Load())
        {
            if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)) continue;
            if (result.Count >= MaxEntries) break;
            result.Add(existing);
        }

        Save(result);
        return result;
    }

    public static IReadOnlyList<string> Clear()
    {
        Save([]);
        return [];
    }

    private static void Save(List<string> cities)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var json = JsonSerializer.Serialize(
                new { cities },
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 忽略
        }
    }
}