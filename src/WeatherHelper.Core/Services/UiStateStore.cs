using System.Text;
using System.Text.Json;
using WeatherHelper.Json;

namespace WeatherHelper.Services;

/// <summary>桌宠和界面的零碎状态。</summary>
public sealed record UiState(int? PetX, int? PetY, bool AutoRefresh = true, bool PetDarkMode = false);

/// <summary>
/// 桌宠的位置、配色（深/浅色模式）、自动刷新开关这类界面状态，
/// 持久化在 <c>%APPDATA%\WeatherHelper\ui.json</c>。
///
/// 刻意不放进 exe 同级的 <c>settings.json</c>：那个目录可能是只读的（装到 Program Files 下就是），
/// 而且 <see cref="AppSettings"/> 本来就只有读没有写。
/// 读写失败一律静默忽略 —— 丢个窗口位置是小事，不能因此让程序报错。
/// </summary>
public static class UiStateStore
{
    public static string FilePath => Path.Combine(HistoryStore.DirectoryPath, "ui.json");

    /// <summary>读取；文件缺失或损坏都退回默认值（位置为空、自动刷新开、桌宠浅色）。</summary>
    public static UiState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UiState(null, null);

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            using var document = JsonHelp.Parse(json);
            if (document is null) return new UiState(null, null);

            var root = document.RootElement;
            return new UiState(
                root.Int("petX"),
                root.Int("petY"),
                root.Bool("autoRefresh") ?? true,
                root.Bool("petDarkMode") ?? false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiState(null, null);
        }
    }

    public static void Save(UiState state)
    {
        try
        {
            Directory.CreateDirectory(HistoryStore.DirectoryPath);

            // 用匿名对象序列化，保证键名是小写 —— 直接把 record 丢给 JsonSerializer 会写成 PascalCase，
            // 和上面 Load 里读的键名对不上。
            var json = JsonSerializer.Serialize(
                new { petX = state.PetX, petY = state.PetY, autoRefresh = state.AutoRefresh, petDarkMode = state.PetDarkMode },
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 忽略
        }
    }
}
