using CommunityToolkit.Mvvm.Messaging;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using ShopManager.Services;
using ShopManager.ViewModels;
using ShopManager.Views.ShopSelection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ShopManager.Views;

public partial class MainWindow : Window
{
    private readonly AppSnackbarService _snackbarService;

    public MainWindow(MainViewModel viewModel, AppSnackbarService snackbarService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _snackbarService = snackbarService;

        try
        {
            Icon = new BitmapImage(new Uri("pack://application:,,,/Resources/app.ico"));
        }
        catch { }

        // WindowStyle=None 最大化時需限制在工作區範圍內，避免蓋住工作列。
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;

        // 將 Snackbar 的 MessageQueue 連接到共用服務。
        Loaded += (_, _) =>
        {
            var queue = new SnackbarMessageQueue(TimeSpan.FromSeconds(3));
            RootSnackbar.MessageQueue = queue;
            _snackbarService.SetQueue(queue);

            _ = viewModel.InitializeAsync();
        };

        // 監聽店鋪關閉事件，重新顯示選擇視窗。
        WeakReferenceMessenger.Default.Register<ShopClosedMessage>(this, (r, _) =>
        {
            var window = (MainWindow)r;
            window.Dispatcher.Invoke(window.HandleShopClosed);
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

        var vm = (MainViewModel)DataContext;
        vm.ResetAfterShopChange();
        _ = vm.InitializeAsync();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) =>
        Close();
}
