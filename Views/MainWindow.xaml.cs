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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace ShopManager.Views;

public partial class MainWindow : Window
{
    private readonly AppSnackbarService _snackbarService;

    public MainWindow(MainViewModel viewModel, AppSnackbarService snackbarService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _snackbarService = snackbarService;

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

            var totalBytes = dlResp.Content.Headers.ContentLength;
            var tempPath   = Path.Combine(Path.GetTempPath(), assetName!);

            // 安裝檔通常有數十 MB，CopyToAsync 之前完全沒有進度顯示，看起來像整個程式卡住；
            // 改成逐塊複製並即時回報進度。
            var progressWindow = new UpdateDownloadWindow { Owner = this };
            progressWindow.Show();
            try
            {
                await using var httpStream = await dlResp.Content.ReadAsStreamAsync();
                await using var fs = File.Create(tempPath);
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await httpStream.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    progressWindow.SetProgress(totalRead, totalBytes);
                }
            }
            finally
            {
                progressWindow.Close();
            }

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ConstrainMaximizeToWorkArea);
    }

    // WindowStyle=None 的視窗最大化時，Windows 預設給整個螢幕範圍而蓋住工作列；改成視窗所在螢幕的工作區
    private static IntPtr ConstrainMaximizeToWorkArea(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var work = info.rcWork;
        // 工作列自動隱藏時工作區等於整個螢幕；保留工作列那一側 2px，滑鼠碰到邊緣時工作列才叫得出來
        if (work.Equals(info.rcMonitor) && NativeMethods.TryGetAutoHideTaskbarEdge(out var edge))
        {
            switch (edge)
            {
                case NativeMethods.ABE_LEFT:   work.Left   += 2; break;
                case NativeMethods.ABE_TOP:    work.Top    += 2; break;
                case NativeMethods.ABE_RIGHT:  work.Right  -= 2; break;
                case NativeMethods.ABE_BOTTOM: work.Bottom -= 2; break;
            }
        }

        var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X = work.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = work.Top - info.rcMonitor.Top;
        mmi.ptMaxSize.X = work.Right - work.Left;
        mmi.ptMaxSize.Y = work.Bottom - work.Top;
        Marshal.StructureToPtr(mmi, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    private static class NativeMethods
    {
        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const int ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
        private const int ABM_GETSTATE = 4, ABM_GETTASKBARPOS = 5, ABS_AUTOHIDE = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public IntPtr lParam; }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        public static bool TryGetAutoHideTaskbarEdge(out int edge)
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            edge = ABE_BOTTOM;
            if (((uint)SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) == 0) return false;
            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) != UIntPtr.Zero) edge = (int)data.uEdge;
            return true;
        }
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
