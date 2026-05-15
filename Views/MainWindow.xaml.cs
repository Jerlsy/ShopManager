using CommunityToolkit.Mvvm.Messaging;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using ShopManager.Services;
using ShopManager.ViewModels;
using ShopManager.Views.ShopSelection;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;

namespace ShopManager.Views;

public partial class MainWindow : Window
{
    private readonly AppSnackbarService _snackbarService;

    public MainWindow(MainViewModel viewModel, AppSnackbarService snackbarService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _snackbarService = snackbarService;

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
            _ = CheckForUpdatesAsync();
        };

        // ContentRendered 在視窗完整渲染後才觸發，比 Loaded 更晚，
        // 此時 WPF 不會再覆蓋圖示，是修正透明視窗 taskbar 圖示的正確時機。
        ContentRendered += (_, _) => ForceRefreshTaskbarIcon();

        // 監聽店鋪關閉事件，重新顯示選擇視窗。
        WeakReferenceMessenger.Default.Register<ShopClosedMessage>(this, (r, _) =>
        {
            var window = (MainWindow)r;
            window.Dispatcher.Invoke(window.HandleShopClosed);
        });
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var http = App.Services.GetRequiredService<HttpClient>();
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/Jerlsy/ShopManager/releases/latest");
            req.Headers.UserAgent.ParseAdd("ShopManager");

            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] API 回應 {(int)resp.StatusCode}");
                return;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tagName = root.GetProperty("tag_name").GetString()!;
            var latestVersion = Version.Parse(tagName.TrimStart('v'));
            var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
            var currentVersion = new Version(asm.Major, asm.Minor, asm.Build);

            if (latestVersion <= currentVersion) return;

            var result = MessageBox.Show(
                $"發現新版本 {tagName}，是否立即下載並更新？\n（下載完成後將自動執行安裝程式）",
                "軟體更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes) return;

            string? downloadUrl = null;
            string? assetName = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString()!;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    assetName = name;
                    break;
                }
            }
            if (downloadUrl is null)
            {
                MessageBox.Show("找不到安裝檔，請至 GitHub 手動下載。", "更新失敗",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var dlReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            dlReq.Headers.UserAgent.ParseAdd("ShopManager");

            var dlResp = await http.SendAsync(dlReq, HttpCompletionOption.ResponseHeadersRead);
            dlResp.EnsureSuccessStatusCode();

            var tempPath = Path.Combine(Path.GetTempPath(), assetName!);
            await using (var fs = File.Create(tempPath))
                await dlResp.Content.CopyToAsync(fs);

            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] 失敗: {ex.Message}");
            // 離線或 GitHub 無法連線時靜默略過
        }
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

    private void ForceRefreshTaskbarIcon()
    {
        var icon = Icon;
        Icon = null;
        Icon = icon;
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) =>
        Close();
}
