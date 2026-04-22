using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MaterialDesignThemes.Wpf;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.Views.EmployeeManagement;
using ShopManager.Views.SalarySettings;
using ShopManager.Views.Schedule;
using ShopManager.Views.ShiftSettings;
using ShopManager.Views.ShopSettings;

namespace ShopManager.ViewModels;

/// <summary>系統設定完成後發送的通知。</summary>
public class SystemConfiguredMessage
{
    public string ShopName { get; init; } = string.Empty;
    public byte[]? LogoPhotoData { get; init; }
}

/// <summary>店鋪關閉後發送的通知。</summary>
public class ShopClosedMessage { }

/// <summary>導覽項目。</summary>
public record NavItem(string Label, PackIconKind Icon, Type PageType, bool RequiresConfig = true);

public partial class MainViewModel : ObservableObject
{
    private readonly ShopSettingService _shopService;
    private readonly ShopContext _shopContext;
    private readonly NavigationService _navigation;

    [ObservableProperty] private string _shopName = "店鋪管理系統";
    [ObservableProperty] private byte[]? _shopLogoData;
    [ObservableProperty] private bool _isSystemConfigured;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private bool _isNavExpanded = true;

    [RelayCommand]
    private void ToggleNav() => IsNavExpanded = !IsNavExpanded;

    public object? CurrentContent => _navigation.CurrentContent;

    public List<NavItem> AllNavItems { get; } = new()
    {
        new("店舖設定", PackIconKind.Cog, typeof(ShopSettingPage), false),
        new("班別設定", PackIconKind.ClockOutline, typeof(ShiftSettingPage)),
        new("薪資設定", PackIconKind.CurrencyUsd, typeof(SalarySettingPage)),
        new("員工資料管理", PackIconKind.AccountGroup, typeof(EmployeeListPage)),
        new("排班管理", PackIconKind.CalendarMonth, typeof(SchedulePage)),
    };

    public IEnumerable<NavItem> VisibleNavItems =>
        AllNavItems.Where(item => !item.RequiresConfig || IsSystemConfigured);

    public MainViewModel(
        ShopSettingService shopService,
        ShopContext shopContext,
        NavigationService navigation)
    {
        _shopService = shopService;
        _shopContext = shopContext;
        _navigation = navigation;
        ShopName = _shopContext.ShopName;

        _navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NavigationService.CurrentContent))
                OnPropertyChanged(nameof(CurrentContent));
        };

        WeakReferenceMessenger.Default.Register<SystemConfiguredMessage>(this, (r, m) =>
        {
            var vm = (MainViewModel)r;
            vm.IsSystemConfigured = true;
            vm.ShopName = m.ShopName;
            vm.ShopLogoData = m.LogoPhotoData;
            vm.OnPropertyChanged(nameof(VisibleNavItems));
        });
    }

    [RelayCommand]
    private void LeaveShop()
    {
        WeakReferenceMessenger.Default.Send(new ShopClosedMessage());
    }

    [RelayCommand]
    private void ExitApp()
    {
        System.Windows.Application.Current.Shutdown();
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null)
            _navigation.Navigate(value.PageType);
    }

    public async Task InitializeAsync()
    {
        var shop = await _shopService.GetAsync();
        if (shop is not null)
        {
            ShopName = shop.Name;
            ShopLogoData = shop.LogoPhotoData;
            IsSystemConfigured = true;
            OnPropertyChanged(nameof(VisibleNavItems));
        }

        SelectedNavItem = AllNavItems[0];
    }

    public void ResetAfterShopChange()
    {
        _navigation.ClearCache();
        IsSystemConfigured = false;
        OnPropertyChanged(nameof(VisibleNavItems));
    }
}
