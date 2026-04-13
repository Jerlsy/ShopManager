using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShopManager.Data;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace ShopManager.ViewModels;

public partial class SystemSettingViewModel(
    ShopSettingService service,
    ISnackbarService snackbarService,
    IContentDialogService contentDialogService,
    AppDbContext db,
    ShopContext shopContext) : ObservableObject
{
    // ── 店鋪設定 ──────────────────────────────
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

    // ── 行事曆設定 ────────────────────────────
    [ObservableProperty] private int _weekStartDay = 1;
    [ObservableProperty] private bool _nationalHolidaysOff = true;

    public ObservableCollection<DayOfWeekOption> ClosedDayOptions { get; } = new()
    {
        new(DayOfWeek.Monday, "周一"),
        new(DayOfWeek.Tuesday, "周二"),
        new(DayOfWeek.Wednesday, "周三"),
        new(DayOfWeek.Thursday, "周四"),
        new(DayOfWeek.Friday, "周五"),
        new(DayOfWeek.Saturday, "周六"),
        new(DayOfWeek.Sunday, "周日"),
    };

    public static List<WeekStartOption> WeekStartOptions { get; } = new()
    {
        new(0, "周日"),
        new(1, "周一"),
    };

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
        snackbarService.Show("儲存成功", "店舖設定已更新",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
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

        var result = await contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = "關閉店鋪",
                Content = $"確定要關閉「{shopName}」嗎？\n\n" +
                          "此操作將永久刪除該店鋪的所有資料，包含：\n" +
                          "  • 班別設定\n" +
                          "  • 薪資設定\n" +
                          "  • 員工資料\n" +
                          "  • 排班記錄\n\n" +
                          "此操作無法復原，請謹慎確認。",
                PrimaryButtonText = "確認關閉",
                CloseButtonText = "取消",
            });

        if (result != ContentDialogResult.Primary) return;

        await db.DeleteShopDataAsync(shopContext.ShopId);

        WeakReferenceMessenger.Default.Send(new ShopClosedMessage());
    }
}

/// <summary>一周起始日選項</summary>
public record WeekStartOption(int Value, string Label);

/// <summary>店休日勾選項目</summary>
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
