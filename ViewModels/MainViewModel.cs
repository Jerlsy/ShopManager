using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using ShopManager.Models;
using ShopManager.Services;

namespace ShopManager.ViewModels;

/// <summary>系統設定完成後發送的通知</summary>
public class SystemConfiguredMessage
{
    public string ShopName { get; init; } = string.Empty;
}

/// <summary>店鋪關閉後發送的通知</summary>
public class ShopClosedMessage { }

public partial class MainViewModel : ObservableObject
{
    private readonly ShopSettingService _shopService;
    private readonly ShopContext _shopContext;

    [ObservableProperty] private string _shopName = "店鋪管理系統";
    [ObservableProperty] private bool _isSystemConfigured;

    public MainViewModel(ShopSettingService shopService, ShopContext shopContext)
    {
        _shopService = shopService;
        _shopContext = shopContext;

        // 使用選擇視窗傳入的店鋪名稱做初始值
        ShopName = _shopContext.ShopName;

        WeakReferenceMessenger.Default.Register<SystemConfiguredMessage>(this, (r, m) =>
        {
            var vm = (MainViewModel)r;
            vm.IsSystemConfigured = true;
            vm.ShopName = m.ShopName;
        });
    }

    public async Task InitializeAsync()
    {
        var shop = await _shopService.GetAsync();
        if (shop is not null)
        {
            ShopName = shop.Name;
            IsSystemConfigured = true;
        }
    }
}
