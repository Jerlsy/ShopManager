using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MaterialDesignThemes.Wpf;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.Views.EmployeeManagement;
using ShopManager.Views.Salary;
using ShopManager.Views.SalarySettings;
using ShopManager.Views.Schedule;
using ShopManager.Views.ShiftSettings;
using ShopManager.Views.ShopSettings;
using System.Windows;
using System.Windows.Threading;

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
    private readonly IAppDialogService _dialogService;
    private readonly SystemSettingViewModel _systemSettingVm;

    [ObservableProperty] private string _shopName = "店鋪管理系統";
    [ObservableProperty] private byte[]? _shopLogoData;
    [ObservableProperty] private bool _isSystemConfigured;
    [ObservableProperty] private NavItem? _selectedNavItem;
    [ObservableProperty] private bool _isNavExpanded = true;

    private NavItem? _currentNavItem;
    private NavItem? _pendingNavItem;
    private bool _blockNavCheck;

    public GridLength NavColumnWidth =>
        IsNavExpanded
            ? new GridLength((double)Application.Current.Resources["LayoutNavExpandedWidth"])
            : new GridLength(44);

    partial void OnIsNavExpandedChanged(bool value) => OnPropertyChanged(nameof(NavColumnWidth));

    // IsSystemConfigured 是 VisibleNavItems 的唯一變數，連動處理。
    // 之所以集中在此：手動在多處 OnPropertyChanged(nameof(VisibleNavItems)) 會在儲存後
    // 觸發 ListBox 重評 ItemsSource，導致 SelectedItem 暫時被清空再回填，
    // 讓 OnSelectedNavItemChanged 在 HasUnsavedChanges 尚未歸零前再度觸發未儲存對話框。
    partial void OnIsSystemConfiguredChanged(bool value) => OnPropertyChanged(nameof(VisibleNavItems));

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
        new("薪資計算", PackIconKind.CashMultiple, typeof(SalaryPage)),
    };

    public IEnumerable<NavItem> VisibleNavItems =>
        AllNavItems.Where(item => !item.RequiresConfig || IsSystemConfigured);

    public MainViewModel(
        ShopSettingService shopService,
        ShopContext shopContext,
        NavigationService navigation,
        IAppDialogService dialogService,
        SystemSettingViewModel systemSettingVm)
    {
        _shopService = shopService;
        _shopContext = shopContext;
        _navigation = navigation;
        _dialogService = dialogService;
        _systemSettingVm = systemSettingVm;
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
        if (value is null || _blockNavCheck) return;

        if (_systemSettingVm.HasUnsavedChanges &&
            _currentNavItem?.PageType == typeof(ShopSettingPage))
        {
            var previous  = _currentNavItem;
            _pendingNavItem = value;

            _blockNavCheck = true;
            SelectedNavItem = previous;
            _blockNavCheck = false;

            Dispatcher.CurrentDispatcher.InvokeAsync(HandleUnsavedChangesNavigationAsync);
            return;
        }

        _currentNavItem = value;
        _navigation.Navigate(value.PageType);
    }

    private async Task HandleUnsavedChangesNavigationAsync()
    {
        var result = await _dialogService.ShowUnsavedChangesAsync();
        var target  = _pendingNavItem;
        _pendingNavItem = null;

        if (result is null || target is null) return; // 取消

        if (result == true)
            await _systemSettingVm.SaveAsync();

        _blockNavCheck = true;
        SelectedNavItem = target;
        _blockNavCheck = false;
        _currentNavItem = target;
        _navigation.Navigate(target.PageType);
    }

    public async Task InitializeAsync()
    {
        var shop = await _shopService.GetAsync();
        if (shop is not null)
        {
            ShopName = shop.Name;
            ShopLogoData = shop.LogoPhotoData;
            IsSystemConfigured = true;
        }

        SelectedNavItem = AllNavItems[0];
    }

    public void ResetAfterShopChange()
    {
        _navigation.ClearCache();
        IsSystemConfigured = false;
    }
}
