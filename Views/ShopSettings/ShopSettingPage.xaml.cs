using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ShopManager.ViewModels;
using ShopManager.Views.Line;
using System.Windows;
using System.Windows.Controls;

namespace ShopManager.Views.ShopSettings;

public partial class ShopSettingPage : UserControl
{
    private readonly SystemSettingViewModel _viewModel;

    public ShopSettingPage(SystemSettingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
        viewModel.LineTestSucceeded += OnLineTestSucceeded;
    }

    private async void OnLineTestSucceeded(object? sender, string token)
    {
        var win = App.Services.GetRequiredService<LineFollowerWindow>();
        win.Owner = Window.GetWindow(this);
        win.Show();
        await win.ViewModel.InitAsync(token, _viewModel.LineWorkerUrl, _viewModel.LineWorkerApiKey);
    }

    private void TokenHelp_Click(object sender, RoutedEventArgs e)
    {
        var win = new LineTokenHelpWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }

    private void WorkerHelp_Click(object sender, RoutedEventArgs e)
    {
        var win = new CloudflareWorkerHelpWindow { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }

    private async void ViewFollowers_Click(object sender, RoutedEventArgs e)
    {
        var win = App.Services.GetRequiredService<LineFollowerWindow>();
        win.Owner = Window.GetWindow(this);
        win.Show();
        await win.ViewModel.InitAsync(
            _viewModel.LineChannelAccessToken,
            _viewModel.LineWorkerUrl,
            _viewModel.LineWorkerApiKey);
    }

    private void PickLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "圖片檔案|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
            Title = "選擇店鋪 Logo"
        };
        if (dlg.ShowDialog() != true) return;

        var cropWin = new LogoCropWindow(dlg.FileName)
        {
            Owner = Window.GetWindow(this)
        };
        if (cropWin.ShowDialog() != true || cropWin.CroppedPng == null) return;

        _viewModel.SetLogoPhoto(cropWin.CroppedPng);
    }
}
