using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.ViewModels;
using ShopManager.Views.Line;
using System.Windows;
using System.Windows.Controls;

namespace ShopManager.Views.EmployeeManagement;

public partial class EmployeeListPage : UserControl
{
    private readonly EmployeeViewModel _viewModel;

    public EmployeeListPage(EmployeeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
        viewModel.OpenLineBindingRequested += OnOpenLineBindingRequested;
    }

    private async void OnOpenLineBindingRequested(object? sender, EventArgs e)
    {
        var shopSetting = await App.Services
            .GetRequiredService<ShopSettingService>()
            .GetAsync();

        var token = shopSetting?.LineChannelAccessToken;
        var workerUrl = shopSetting?.LineWorkerUrl;
        var apiKey = shopSetting?.LineWorkerApiKey;

        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show("尚未設定 LINE Channel Access Token，請至店鋪設定頁面輸入。",
                "未設定 LINE Token", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(workerUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show("尚未設定 Cloudflare Worker URL 或 API Key，請至店鋪設定頁面輸入。",
                "未設定 Worker 設定", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var win = App.Services.GetRequiredService<LineFollowerWindow>();
        win.ViewModel.IsSelectMode = true;
        win.Owner = Window.GetWindow(this);

        win.ViewModel.FollowerSelected += (_, item) =>
            _viewModel.ApplyLineBinding(item.UserId, item.DisplayName, item.PictureUrl);

        win.Show();
        await win.ViewModel.InitAsync(token, workerUrl, apiKey);
    }

    private async void EmployeeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not Employee emp) return;
        await _viewModel.StartEditAsync(emp);
    }

    private void PickAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "圖片檔案|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
            Title = "選擇大頭貼照片"
        };
        if (dlg.ShowDialog() != true) return;

        var cropWin = new AvatarCropWindow(dlg.FileName)
        {
            Owner = Window.GetWindow(this)
        };
        if (cropWin.ShowDialog() != true || cropWin.CroppedPng == null) return;

        _viewModel.SetAvatarPhoto(cropWin.CroppedPng);
    }
}
