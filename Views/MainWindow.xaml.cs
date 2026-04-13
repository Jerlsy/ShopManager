using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using ShopManager.ViewModels;
using ShopManager.Views.ShopSelection;
using ShopManager.Views.ShopSettings;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace ShopManager.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        InitializeComponent();
        DataContext = viewModel;

        snackbarService.SetSnackbarPresenter(RootSnackbar);
        contentDialogService.SetDialogHost(RootContentDialog);

        _ = viewModel.InitializeAsync();

        // 監聽店鋪關閉事件，重新顯示選擇視窗
        WeakReferenceMessenger.Default.Register<ShopClosedMessage>(this, (r, _) =>
        {
            var window = (MainWindow)r;
            window.Dispatcher.Invoke(() => window.HandleShopClosed());
        });
    }

    private void HandleShopClosed()
    {
        var selectionWindow = App.Services.GetRequiredService<ShopSelectionWindow>();
        var result = selectionWindow.ShowDialog();

        if (result != true)
        {
            Application.Current.Shutdown();
            return;
        }

        // 重新初始化 MainViewModel 狀態
        var vm = (MainViewModel)DataContext;
        vm.IsSystemConfigured = false;
        _ = vm.InitializeAsync();

        RootNavigation.Navigate(typeof(ShopSettingPage));
    }

    private void RootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        // 設定頁面工廠，讓 NavigationView 透過 DI 建立頁面實例
        RootNavigation.SetPageProviderService(new PageServiceFactory());

        // 預設導覽到「店舖設定」
        RootNavigation.Navigate(typeof(ShopSettingPage));
    }
}

/// <summary>
/// 讓 wpfui NavigationView 透過 DI 容器建立頁面，
/// 避免頁面需要無參數建構子
/// </summary>
public class PageServiceFactory : Wpf.Ui.Abstractions.INavigationViewPageProvider
{
    public object? GetPage(Type pageType) =>
        App.Services.GetService(pageType);
}
