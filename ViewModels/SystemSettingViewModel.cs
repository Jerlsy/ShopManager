using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShopManager.Data;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public partial class SystemSettingViewModel(
    ShopSettingService service,
    IAppSnackbarService snackbarService,
    IAppDialogService dialogService,
    AppDbContext db,
    ShopContext shopContext,
    ThemeService themeService) : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private List<ContactInfo> _contactInfos = new();

    public static List<string> ContactTypes { get; } = new()
    {
        "Email", "Facebook", "Instagram", "Line", "WhatsApp",
        "Telegram", "WeChat", "YouTube", "Twitter/X", "TikTok",
        "官方網站", "其他"
    };

    [ObservableProperty] private int _weekStartDay = 1;
    [ObservableProperty] private bool _nationalHolidaysOff = true;
    [ObservableProperty] private string _customPrimaryHex = "#546E7A";
    [ObservableProperty] private string _customSecondaryHex = "#29B6F6";

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
        var setting = await service.GetAsync();
        if (setting is not null)
        {
            Name = setting.Name;
            Address = setting.Address;
            Phone = setting.Phone;
            ContactInfos = new List<ContactInfo>(setting.ContactInfos);
            WeekStartDay = setting.WeekStartDay;
            NationalHolidaysOff = setting.NationalHolidaysOff;

            foreach (var option in ClosedDayOptions)
                option.IsChecked = setting.ClosedDaysOfWeek.Contains((int)option.Day);
        }

        CustomPrimaryHex = themeService.CustomPrimaryHex;
        CustomSecondaryHex = themeService.CustomSecondaryHex;
        NotifyThemeChanged();
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
            ContactInfos = ContactInfos,
            WeekStartDay = WeekStartDay,
            ClosedDaysOfWeek = closedDays,
            NationalHolidaysOff = NationalHolidaysOff,
        };

        await service.SaveAsync(setting);
        WeakReferenceMessenger.Default.Send(new SystemConfiguredMessage { ShopName = Name });
        snackbarService.ShowSuccess("店舖設定已儲存");
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
