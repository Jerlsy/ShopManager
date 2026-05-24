using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShopManager.Data;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ShopManager.ViewModels;

public enum LineTestState { None, Testing, Success, Failed }

public partial class SystemSettingViewModel(
    ShopSettingService service,
    IAppSnackbarService snackbarService,
    IAppDialogService dialogService,
    AppDbContext db,
    ShopContext shopContext,
    ThemeService themeService,
    AppearanceService appearanceService,
    LineService lineService) : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private byte[]? _logoPhotoData;
    [ObservableProperty] private List<ContactInfo> _contactInfos = new();

    public static List<string> ContactTypes { get; } = new()
    {
        "Email", "Facebook", "Instagram", "Line", "WhatsApp",
        "Telegram", "WeChat", "YouTube", "Twitter/X", "TikTok",
        "官方網站", "其他"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedWeekStartDay))]
    private int _weekStartDay = 1;

    public WeekStartOption? SelectedWeekStartDay
    {
        get => WeekStartOptions.FirstOrDefault(o => o.Value == WeekStartDay);
        set { if (value is not null) WeekStartDay = value.Value; }
    }
    [ObservableProperty] private bool _nationalHolidaysOff = true;

    // ── LINE 推播設定 ────────────────────────────────────────────────────────
    [ObservableProperty] private string _lineChannelAccessToken = string.Empty;
    [ObservableProperty] private string _lineWorkerUrl = string.Empty;
    [ObservableProperty] private string _lineWorkerApiKey = string.Empty;
    [ObservableProperty] private string _lineWelcomeMessage = string.Empty;
    [ObservableProperty] private string _lineResignMessage = string.Empty;
    [ObservableProperty] private List<OwnerLineBinding> _ownerLineBindings = new();
    [ObservableProperty] private bool _isLineConfigUnlocked;

    // ── 備註 ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private LineTestState _lineTestState = LineTestState.None;

    // ── 未儲存變更追蹤 ───────────────────────────────────────────────────────
    [ObservableProperty] private bool _hasUnsavedChanges;
    private bool _suppressDirty;
    private bool _closedDayOptionsWired;

    private static readonly HashSet<string?> _volatileProps =
    [
        nameof(HasUnsavedChanges),
        nameof(LineTestState), nameof(LineTestMessage),
        nameof(IsLineTesting), nameof(IsLineTestResultVisible),
        nameof(IsLineTestSuccess), nameof(IsLineTestFailed),
        nameof(CurrentThemeName), nameof(CurrentThemeAccent),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_suppressDirty && !_volatileProps.Contains(e.PropertyName))
            HasUnsavedChanges = true;
    }
    [ObservableProperty] private string _lineTestMessage = string.Empty;

    /// <summary>測試成功後觸發，View 負責開啟 LineFollowerWindow</summary>
    public event EventHandler<string>? LineTestSucceeded;

    public bool IsLineTesting => LineTestState == LineTestState.Testing;
    public bool IsLineTestResultVisible => LineTestState != LineTestState.None;
    public bool IsLineTestSuccess => LineTestState == LineTestState.Success;
    public bool IsLineTestFailed => LineTestState == LineTestState.Failed;
    [ObservableProperty] private string _customPrimaryHex = "#546E7A";
    [ObservableProperty] private string _customSecondaryHex = "#29B6F6";

    // ── 外觀設定 ────────────────────────────────────────────────────────────
    [ObservableProperty] private double _baseFontSize = 15.0;
    [ObservableProperty] private FontOption? _selectedFontFamily;

    public IReadOnlyList<FontOption> AvailableFontFamilies => AppearanceService.AvailableFonts;

    public ObservableCollection<DayOfWeekOption> ClosedDayOptions { get; } = new()
    {
        new(DayOfWeek.Monday, "週一"),
        new(DayOfWeek.Tuesday, "週二"),
        new(DayOfWeek.Wednesday, "週三"),
        new(DayOfWeek.Thursday, "週四"),
        new(DayOfWeek.Friday, "週五"),
        new(DayOfWeek.Saturday, "週六"),
        new(DayOfWeek.Sunday, "週日"),
    };

    public static List<WeekStartOption> WeekStartOptions { get; } = new()
    {
        new(0, "週日"),
        new(1, "週一"),
    };

    public IReadOnlyList<ThemePreset> ThemePresets => themeService.Presets;
    public string CurrentThemeName => themeService.CurrentThemeName;
    public AppThemeAccent CurrentThemeAccent => themeService.CurrentTheme;

    public async Task LoadAsync()
    {
        _suppressDirty = true;
        try
        {
            var setting = await service.GetAsync();
            if (setting is not null)
            {
                Name = setting.Name;
                Address = setting.Address;
                Phone = setting.Phone;
                LogoPhotoData = setting.LogoPhotoData;
                ContactInfos = new List<ContactInfo>(setting.ContactInfos);
                WeekStartDay = setting.WeekStartDay;
                NationalHolidaysOff = setting.NationalHolidaysOff;

                foreach (var option in ClosedDayOptions)
                    option.IsChecked = setting.ClosedDaysOfWeek.Contains((int)option.Day);

                LineChannelAccessToken = setting.LineChannelAccessToken ?? string.Empty;
                LineWorkerUrl = setting.LineWorkerUrl ?? string.Empty;
                LineWorkerApiKey = setting.LineWorkerApiKey ?? string.Empty;
                LineWelcomeMessage = setting.LineWelcomeMessage ?? string.Empty;
                LineResignMessage = setting.LineResignMessage ?? string.Empty;
                IsLineConfigUnlocked = !string.IsNullOrWhiteSpace(setting.LineChannelAccessToken);
                OwnerLineBindings = new List<OwnerLineBinding>(setting.OwnerLineBindings);
                Notes = setting.Notes;
            }

            CustomPrimaryHex = themeService.CustomPrimaryHex;
            CustomSecondaryHex = themeService.CustomSecondaryHex;
            NotifyThemeChanged();

            BaseFontSize = appearanceService.BaseFontSize;
            SelectedFontFamily = AppearanceService.AvailableFonts
                .FirstOrDefault(f => f.Name == appearanceService.FontFamilyName)
                ?? AppearanceService.AvailableFonts[0];

            if (!_closedDayOptionsWired)
            {
                foreach (var opt in ClosedDayOptions)
                    opt.PropertyChanged += (_, _) => { if (!_suppressDirty) HasUnsavedChanges = true; };
                _closedDayOptionsWired = true;
            }
        }
        finally
        {
            _suppressDirty = false;
            HasUnsavedChanges = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var closedDays = ClosedDayOptions
            .Where(o => o.IsChecked)
            .Select(o => (int)o.Day)
            .ToList();

        var setting = new ShopSetting
        {
            Name = Name,
            Address = Address,
            Phone = Phone,
            LogoPhotoData = LogoPhotoData,
            ContactInfos = ContactInfos,
            WeekStartDay = WeekStartDay,
            ClosedDaysOfWeek = closedDays,
            NationalHolidaysOff = NationalHolidaysOff,
            LineChannelAccessToken = string.IsNullOrWhiteSpace(LineChannelAccessToken) ? null : LineChannelAccessToken,
            LineWorkerUrl = string.IsNullOrWhiteSpace(LineWorkerUrl) ? null : LineWorkerUrl,
            LineWorkerApiKey = string.IsNullOrWhiteSpace(LineWorkerApiKey) ? null : LineWorkerApiKey,
            LineWelcomeMessage = string.IsNullOrWhiteSpace(LineWelcomeMessage) ? null : LineWelcomeMessage,
            LineResignMessage = string.IsNullOrWhiteSpace(LineResignMessage) ? null : LineResignMessage,
            OwnerLineBindings = OwnerLineBindings,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes,
        };

        await service.SaveAsync(setting);

        // 套用外觀設定
        appearanceService.SetBaseFontSize(BaseFontSize);
        if (SelectedFontFamily is not null)
            appearanceService.SetFontFamily(SelectedFontFamily.Name);

        WeakReferenceMessenger.Default.Send(new SystemConfiguredMessage { ShopName = Name, LogoPhotoData = LogoPhotoData });
        snackbarService.ShowSuccess("店舖設定已儲存");
        HasUnsavedChanges = false;
    }

    public void SetLogoPhoto(byte[] data) => LogoPhotoData = data;

    [RelayCommand]
    public async Task TestLineConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(LineChannelAccessToken))
        {
            LineTestState = LineTestState.Failed;
            LineTestMessage = "請先輸入 Channel Access Token";
            return;
        }
        LineTestState = LineTestState.Testing;
        LineTestMessage = "測試中...";
        OnPropertyChanged(nameof(IsLineTesting));
        OnPropertyChanged(nameof(IsLineTestResultVisible));
        OnPropertyChanged(nameof(IsLineTestSuccess));
        OnPropertyChanged(nameof(IsLineTestFailed));
        var (success, message) = await lineService.TestConnectionAsync(LineChannelAccessToken);
        LineTestState = success ? LineTestState.Success : LineTestState.Failed;
        LineTestMessage = message;
        if (success)
        {
            IsLineConfigUnlocked = true;
            LineTestSucceeded?.Invoke(this, LineChannelAccessToken);
        }
        OnPropertyChanged(nameof(IsLineTesting));
        OnPropertyChanged(nameof(IsLineTestResultVisible));
        OnPropertyChanged(nameof(IsLineTestSuccess));
        OnPropertyChanged(nameof(IsLineTestFailed));
    }

    [RelayCommand]
    public void AddContact()
    {
        ContactInfos = new List<ContactInfo>(ContactInfos) { new ContactInfo() };
    }

    [RelayCommand]
    public void RemoveContact(ContactInfo contact)
    {
        var list = new List<ContactInfo>(ContactInfos);
        list.Remove(contact);
        ContactInfos = list;
    }

    /// <summary>新增業主綁定。回傳 false 表示該 UserId 已存在（不重複加入）</summary>
    public bool AddOwnerBinding(OwnerLineBinding item)
    {
        if (OwnerLineBindings.Any(b => b.UserId == item.UserId)) return false;
        OwnerLineBindings = new List<OwnerLineBinding>(OwnerLineBindings) { item };
        return true;
    }

    [RelayCommand]
    public void RemoveOwnerBinding(OwnerLineBinding item)
    {
        var list = new List<OwnerLineBinding>(OwnerLineBindings);
        list.Remove(item);
        OwnerLineBindings = list;
    }

    [RelayCommand]
    public async Task CloseShopAsync()
    {
        var shopName = shopContext.ShopName;

        var confirmed = await dialogService.ShowConfirmAsync(
            "關閉店鋪",
            $"確定要關閉「{shopName}」嗎？\n\n" +
            "此操作將永久刪除該店鋪的所有資料，包含：\n" +
            "  • 班別設定\n  • 薪資設定\n  • 員工資料\n  • 排班記錄\n\n" +
            "此操作無法復原，請謹慎確認。",
            "確認關閉", "取消");

        if (!confirmed) return;

        await db.DeleteShopDataAsync(shopContext.ShopId);
        WeakReferenceMessenger.Default.Send(new ShopClosedMessage());
    }

    [RelayCommand]
    public void SetSkyBlueAccent() => ApplyTheme(AppThemeAccent.SkyBlue);

    [RelayCommand]
    public void SetMintGreenAccent() => ApplyTheme(AppThemeAccent.MintGreen);

    [RelayCommand]
    public void SetAmberOrangeAccent() => ApplyTheme(AppThemeAccent.AmberOrange);

    [RelayCommand]
    public void SetRoyalPurpleAccent() => ApplyTheme(AppThemeAccent.RoyalPurple);

    [RelayCommand]
    public void SetSoftPinkAccent() => ApplyTheme(AppThemeAccent.SoftPink);

    [RelayCommand]
    public void SetVibrantRedAccent() => ApplyTheme(AppThemeAccent.VibrantRed);

    [RelayCommand]
    public void SetOceanBlueAccent() => ApplyTheme(AppThemeAccent.OceanBlue);

    [RelayCommand]
    public void SetMidnightCyanAccent() => ApplyTheme(AppThemeAccent.MidnightCyan);

    [RelayCommand]
    public void ApplyCustomAccent()
    {
        if (!themeService.TrySetCustomAccent(CustomPrimaryHex, CustomSecondaryHex))
        {
            snackbarService.ShowError("自訂色碼格式錯誤，請輸入像 #1976D2 這樣的 HEX 色碼。");
            return;
        }

        CustomPrimaryHex = themeService.CustomPrimaryHex;
        CustomSecondaryHex = themeService.CustomSecondaryHex;
        NotifyThemeChanged();
        snackbarService.ShowSuccess("已套用新的介面配色。");
    }

    private void ApplyTheme(AppThemeAccent accent)
    {
        themeService.SetAccent(accent);
        NotifyThemeChanged();
        snackbarService.ShowSuccess($"已切換為「{themeService.CurrentThemeName}」。");
    }

    private void NotifyThemeChanged()
    {
        OnPropertyChanged(nameof(CurrentThemeName));
        OnPropertyChanged(nameof(CurrentThemeAccent));
    }
}

public record WeekStartOption(int Value, string Label);

public partial class DayOfWeekOption : ObservableObject
{
    public DayOfWeek Day { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;

    public DayOfWeekOption(DayOfWeek day, string label)
    {
        Day = day;
        Label = label;
    }
}
