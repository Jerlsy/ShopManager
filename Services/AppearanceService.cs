using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace ShopManager.Services;

/// <summary>管理全域字體大小與字型，並以 DynamicResource 即時更新 UI。</summary>
public class AppearanceService
{
    // 各語意層級的字體大小比例（相對於 BaseFontSize）
    private static readonly (string Key, double Ratio)[] FontSizeScales =
    [
        ("FontSizeCaption",     0.800),   // 12 @ base=15
        ("FontSizeSmall",       0.867),   // 13 @ base=15
        ("FontSizeSmallBody",   0.933),   // 14 @ base=15
        ("FontSizeBody",        1.000),   // 15 @ base=15
        ("FontSizeBodyPlus",    1.067),   // 16 @ base=15
        ("FontSizeHeading",     1.200),   // 18 @ base=15
        ("FontSizeSubTitle",    1.333),   // 20 @ base=15
        ("FontSizeSectionAlt",  1.467),   // 22 @ base=15
        ("FontSizeSection",     1.600),   // 24 @ base=15
        ("FontSizeDialogTitle", 1.733),   // 26 @ base=15
        ("FontSizeListTitle",   1.867),   // 28 @ base=15
        ("FontSizeWindowTitle", 2.000),   // 30 @ base=15
        ("FontSizePageTitle",   2.133),   // 32 @ base=15
    ];

    public static readonly IReadOnlyList<FontOption> AvailableFonts =
    [
        new("Noto Sans TC",        "pack://application:,,,/Fonts/#Noto Sans TC"),
        new("Microsoft JhengHei",  "Microsoft JhengHei"),
        new("Segoe UI",            "Segoe UI"),
        new("Arial",               "Arial"),
    ];

    private readonly string _preferencePath;

    public AppearanceService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopManager");
        Directory.CreateDirectory(root);
        _preferencePath = Path.Combine(root, "appearance-preferences.json");
        LoadPreference();
    }

    public double BaseFontSize   { get; private set; } = 15.0;
    public string  FontFamilyName { get; private set; } = "Noto Sans TC";

    /// <summary>於應用程式啟動時套用儲存的外觀設定。</summary>
    public void ApplyCurrent()
    {
        ApplyFontSizes();
        ApplyFontFamily();
    }

    public void SetBaseFontSize(double size)
    {
        BaseFontSize = Math.Clamp(Math.Round(size, 1), 11, 22);
        ApplyFontSizes();
        SavePreference();
    }

    public void SetFontFamily(string name)
    {
        FontFamilyName = name;
        ApplyFontFamily();
        SavePreference();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyFontSizes()
    {
        if (Application.Current is null) return;
        var resources = Application.Current.Resources;
        foreach (var (key, ratio) in FontSizeScales)
            resources[key] = Math.Round(BaseFontSize * ratio, 1);
    }

    private void ApplyFontFamily()
    {
        if (Application.Current is null) return;
        var option = AvailableFonts.FirstOrDefault(f => f.Name == FontFamilyName)
                     ?? AvailableFonts[0];

        FontFamily family = new FontFamily(option.PackUri);

        Application.Current.Resources["AppFontFamily"] = family;
    }

    private void LoadPreference()
    {
        if (!File.Exists(_preferencePath)) return;
        try
        {
            var pref = JsonSerializer.Deserialize<AppearancePreference>(File.ReadAllText(_preferencePath));
            if (pref is null) return;
            BaseFontSize   = Math.Clamp(pref.BaseFontSize, 11, 22);
            FontFamilyName = pref.FontFamilyName;
        }
        catch { /* 偏好損毀時沿用預設值 */ }
    }

    private void SavePreference()
    {
        var pref = new AppearancePreference
        {
            BaseFontSize   = BaseFontSize,
            FontFamilyName = FontFamilyName,
        };
        File.WriteAllText(_preferencePath, JsonSerializer.Serialize(pref,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed record FontOption(string Name, string PackUri);

internal sealed class AppearancePreference
{
    public double BaseFontSize   { get; set; } = 15.0;
    public string FontFamilyName { get; set; } = "Noto Sans TC";
}
