using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace ShopManager.Services;

public enum AppThemeAccent
{
    SkyBlue,
    MintGreen,
    AmberOrange,
    RoyalPurple,
    SoftPink,
    VibrantRed,
    OceanBlue,
    MidnightCyan,
    Custom
}

public sealed class ThemePreset
{
    public required AppThemeAccent Accent { get; init; }
    public required string Name { get; init; }
    public required Color Primary { get; init; }
    public required Color Secondary { get; init; }
    public bool IsDark { get; init; }
}

internal sealed class ThemePreference
{
    public AppThemeAccent Accent { get; set; } = AppThemeAccent.SkyBlue;
    public string CustomPrimaryHex { get; set; } = "#546E7A";
    public string CustomSecondaryHex { get; set; } = "#29B6F6";
}

public class ThemeService
{
    private static readonly IReadOnlyList<ThemePreset> PresetList =
    [
        // Secondary 改為跨色相配色，讓背景漸層從主色過渡到不同色調
        new ThemePreset { Accent = AppThemeAccent.SkyBlue,      Name = "晴空藍",  Primary = Color.FromRgb(3, 169, 244),   Secondary = Color.FromRgb(0, 188, 212)  },  // 藍 → 青
        new ThemePreset { Accent = AppThemeAccent.MintGreen,    Name = "翡翠青",  Primary = Color.FromRgb(38, 198, 170),  Secondary = Color.FromRgb(102, 187, 106) },  // 青綠 → 草綠
        new ThemePreset { Accent = AppThemeAccent.AmberOrange,  Name = "暖陽橘",  Primary = Color.FromRgb(255, 171, 0),   Secondary = Color.FromRgb(239, 83, 80)   },  // 琥珀 → 珊瑚紅
        new ThemePreset { Accent = AppThemeAccent.RoyalPurple,  Name = "星夜紫",  Primary = Color.FromRgb(103, 58, 183),  Secondary = Color.FromRgb(236, 64, 122)  },  // 深紫 → 玫瑰粉
        new ThemePreset { Accent = AppThemeAccent.SoftPink,     Name = "晚霞粉",  Primary = Color.FromRgb(233, 30, 99),   Secondary = Color.FromRgb(255, 138, 101) },  // 桃紅 → 蜜桃橘
        new ThemePreset { Accent = AppThemeAccent.VibrantRed,   Name = "活力紅",  Primary = Color.FromRgb(244, 67, 54),   Secondary = Color.FromRgb(255, 109, 0)   },  // 紅 → 深橘
        new ThemePreset { Accent = AppThemeAccent.OceanBlue,    Name = "深海藍",  Primary = Color.FromRgb(21, 101, 192),  Secondary = Color.FromRgb(0, 172, 193)   },  // 深藍 → 青藍
        new ThemePreset { Accent = AppThemeAccent.MidnightCyan, Name = "深夜模式", Primary = Color.FromRgb(20, 30, 48),   Secondary = Color.FromRgb(71, 220, 255), IsDark = true },
    ];

    private readonly string _preferencePath;

    public ThemeService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopManager");
        Directory.CreateDirectory(root);
        _preferencePath = Path.Combine(root, "theme-preferences.json");

        LoadPreference();
    }

    public IReadOnlyList<ThemePreset> Presets => PresetList;
    public AppThemeAccent CurrentTheme { get; private set; } = AppThemeAccent.SkyBlue;
    public bool IsDark => CurrentPreset?.IsDark ?? false;
    public string CustomPrimaryHex { get; private set; } = "#546E7A";
    public string CustomSecondaryHex { get; private set; } = "#29B6F6";

    private ThemePreset? CurrentPreset =>
        CurrentTheme == AppThemeAccent.Custom
            ? null
            : PresetList.FirstOrDefault(x => x.Accent == CurrentTheme);

    public string CurrentThemeName =>
        CurrentTheme == AppThemeAccent.Custom
            ? "自訂配色"
            : CurrentPreset?.Name ?? "晴空藍";

    public void ApplyCurrent()
    {
        if (CurrentTheme == AppThemeAccent.Custom)
        {
            ApplyCustomTheme();
            return;
        }

        SetAccent(CurrentTheme);
    }

    public void SetAccent(AppThemeAccent theme)
    {
        if (theme == AppThemeAccent.Custom)
        {
            ApplyCustomTheme();
            return;
        }

        var preset = PresetList.First(x => x.Accent == theme);
        CurrentTheme = theme;
        ApplyColors(preset.Primary, preset.Secondary, preset.IsDark);
        SavePreference();
    }

    public bool TrySetCustomAccent(string primaryHex, string secondaryHex)
    {
        if (!TryParseHex(primaryHex, out var primary) || !TryParseHex(secondaryHex, out var secondary))
            return false;

        CustomPrimaryHex = NormalizeHex(primaryHex);
        CustomSecondaryHex = NormalizeHex(secondaryHex);
        ApplyCustomTheme();
        return true;
    }

    private void ApplyCustomTheme()
    {
        CurrentTheme = AppThemeAccent.Custom;
        ApplyColors(ParseHex(CustomPrimaryHex), ParseHex(CustomSecondaryHex), false);
        SavePreference();
    }

    private void ApplyColors(Color primary, Color secondary, bool isDark)
    {
        var helper = new PaletteHelper();
        var current = helper.GetTheme();

        current.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        current.SetPrimaryColor(primary);
        current.SetSecondaryColor(secondary);

        helper.SetTheme(current);
        ApplySemanticResources(primary, secondary, isDark);
    }

    private static void ApplySemanticResources(Color primary, Color secondary, bool isDark)
    {
        if (Application.Current is null)
            return;

        var resources = Application.Current.Resources;

        Color shellBackground, shellGradientStart, shellGradientMid, shellGradientEnd;
        Color surface, surfaceAlt, panel, panelAlt;

        if (isDark)
        {
            shellBackground = Color.FromRgb(11, 16, 24);
            shellGradientStart = Color.FromRgb(18, 25, 37);
            shellGradientMid = Color.FromRgb(15, 21, 31);
            shellGradientEnd = Color.FromRgb(10, 15, 23);
            surface = Color.FromRgb(18, 24, 34);
            surfaceAlt = Color.FromRgb(24, 31, 43);
            panel = Color.FromRgb(22, 28, 39);
            panelAlt = Color.FromRgb(27, 35, 48);
        }
        else
        {
            // 淺色模式：背景漸層從主色過渡到次色，飽和度拉高讓色彩可見
            shellBackground    = Mix(Mix(primary, secondary, 0.30), Colors.White, 0.42);
            shellGradientStart = Mix(primary,                        Colors.White, 0.58);
            shellGradientMid   = Mix(Mix(primary, secondary, 0.50), Colors.White, 0.55);
            shellGradientEnd   = Mix(secondary,                      Colors.White, 0.55);

            // 中央主要內容面板（Surface）維持極淺主色染色，確保與深色背景形成強烈對比
            surface    = Mix(primary, Colors.White, 0.04);
            surfaceAlt = Mix(primary, Colors.White, 0.08);
            panel      = Mix(primary, Colors.White, 0.12);
            panelAlt   = Mix(primary, Colors.White, 0.16);
        }

        var border = isDark ? Color.FromArgb(64, 255, 255, 255) : Color.FromArgb(18, 15, 23, 42);
        // 選取/Hover 顏色跟隨主題色（從主色衍生淺染），而非固定藍色
        var selected = isDark ? Color.FromArgb(70, secondary.R, secondary.G, secondary.B) : Mix(primary, Colors.White, 0.15);
        var hover    = isDark ? Color.FromArgb(42, 255, 255, 255)                         : Mix(primary, Colors.White, 0.08);
        var info = isDark ? Color.FromArgb(38, secondary.R, secondary.G, secondary.B) : Color.FromRgb(234, 243, 250);
        var infoForeground = isDark ? Color.FromRgb(220, 244, 255) : Color.FromRgb(49, 74, 97);
        var subtleForeground = isDark ? Color.FromArgb(184, 255, 255, 255) : Color.FromArgb(158, 15, 23, 42);
        var subtleForegroundStrong = isDark ? Color.FromArgb(214, 255, 255, 255) : Color.FromArgb(176, 15, 23, 42);

        SetColor(resources, "AppShellBackgroundColor", shellBackground);
        SetColor(resources, "AppShellGradientStartColor", shellGradientStart);
        SetColor(resources, "AppShellGradientMidColor", shellGradientMid);
        SetColor(resources, "AppShellGradientEndColor", shellGradientEnd);

        SetBrush(resources, "AppShellBackgroundBrush", shellBackground);
        SetBrush(resources, "AppSurfaceBrush", surface);
        SetBrush(resources, "AppSurfaceAltBrush", surfaceAlt);
        SetBrush(resources, "AppPanelBrush", panel);
        SetBrush(resources, "AppPanelAltBrush", panelAlt);
        SetBrush(resources, "AppBorderBrush", border);
        SetBrush(resources, "AppSelectionBrush", selected);
        SetBrush(resources, "AppHoverBrush", hover);
        SetBrush(resources, "AppInfoBrush", info);
        SetBrush(resources, "AppInfoForegroundBrush", infoForeground);
        SetBrush(resources, "AppSubtleForegroundBrush", subtleForeground);
        SetBrush(resources, "AppSubtleForegroundStrongBrush", subtleForegroundStrong);
        SetBrush(resources, "AppPrimaryPreviewBrush", primary);
        SetBrush(resources, "AppSecondaryPreviewBrush", secondary);
    }

    private static Color Mix(Color foreground, Color background, double percentage)
    {
        return Color.FromRgb(
            (byte)(foreground.R * percentage + background.R * (1 - percentage)),
            (byte)(foreground.G * percentage + background.G * (1 - percentage)),
            (byte)(foreground.B * percentage + background.B * (1 - percentage)));
    }

    private void LoadPreference()
    {
        if (!File.Exists(_preferencePath))
            return;

        try
        {
            var preference = JsonSerializer.Deserialize<ThemePreference>(File.ReadAllText(_preferencePath));
            if (preference is null)
                return;

            CurrentTheme = preference.Accent;
            CustomPrimaryHex = preference.CustomPrimaryHex;
            CustomSecondaryHex = preference.CustomSecondaryHex;
        }
        catch
        {
            CurrentTheme = AppThemeAccent.SkyBlue;
            CustomPrimaryHex = "#03A9F4";
            CustomSecondaryHex = "#01579B";
        }
    }

    private void SavePreference()
    {
        var preference = new ThemePreference
        {
            Accent = CurrentTheme,
            CustomPrimaryHex = CustomPrimaryHex,
            CustomSecondaryHex = CustomSecondaryHex,
        };

        File.WriteAllText(_preferencePath, JsonSerializer.Serialize(preference, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static void SetColor(ResourceDictionary resources, string key, Color color) =>
        resources[key] = color;

    private static void SetBrush(ResourceDictionary resources, string key, Color color) =>
        resources[key] = new SolidColorBrush(color);

    private static bool TryParseHex(string value, out Color color)
    {
        try
        {
            color = ParseHex(value);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static Color ParseHex(string value) =>
        (Color)ColorConverter.ConvertFromString(NormalizeHex(value))!;

    private static string NormalizeHex(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('#') ? trimmed.ToUpperInvariant() : $"#{trimmed.ToUpperInvariant()}";
    }
}
