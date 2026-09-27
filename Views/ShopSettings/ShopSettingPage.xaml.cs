using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.ViewModels;
using ShopManager.Views.Dialogs;
using ShopManager.Views.Line;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShopManager.Views.ShopSettings;

public partial class ShopSettingPage : UserControl
{
    private readonly SystemSettingViewModel _viewModel;

    public ShopSettingPage(SystemSettingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) =>
        {
            await viewModel.LoadAsync();
            // WebView2 冷啟動約 3-5 秒，延後到 ApplicationIdle 優先級執行，
            // 讓主視窗先 render；備註區暫時顯示佔位文字，啟動後再渲染
            await Dispatcher.BeginInvoke(
                new Func<Task>(InitNotesPreviewAsync),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // 雲端備份新舊比對：不 await，背景查完才視需要跳提示，不擋這個頁面的載入渲染。
            _ = CheckCloudBackupFreshnessAsync();
        };
        viewModel.LineTestSucceeded += OnLineTestSucceeded;
        viewModel.GoogleBackupRequested  += async (_, _) => await RunGoogleBackupAsync();
        viewModel.GoogleRestoreRequested += async (_, _) => await ConfirmAndRestoreAsync();
        viewModel.GoogleAccountLinked    += async (_, _) => await CheckCloudBackupFreshnessAsync();
        viewModel.ShowDeployGuideRequested += (_, fileId) =>
        {
            var win = new GmailDeployGuideWindow(fileId) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
        };
    }

    private bool _previewReady;

    private async Task InitNotesPreviewAsync()
    {
        if (_previewReady)
        {
            LoadNotesPreview(_viewModel.Notes);
            return;
        }

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopManager", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await NotesPreviewWebView.EnsureCoreWebView2Async(env);
        NotesPreviewWebView.NavigationCompleted += OnPreviewNavigationCompleted;
        // WebView2 (HwndHost) 會吞掉 WM_MOUSEWHEEL，PreviewMouseWheel 不會 fire。
        // 在 HTML 端攔 wheel 並透過 postMessage 把 deltaY 送回 WPF，由我們手動捲外層 ScrollViewer。
        NotesPreviewWebView.CoreWebView2.WebMessageReceived += OnWebViewWheelMessage;
        _previewReady = true;
        LoadNotesPreview(_viewModel.Notes);
    }

    private void OnWebViewWheelMessage(object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!double.TryParse(e.WebMessageAsJson, out var deltaY)) return;

        if (_pageScrollViewer is null)
        {
            DependencyObject? current = NotesPreviewWebView;
            while (current is not null)
            {
                if (current is ScrollViewer sv) { _pageScrollViewer = sv; break; }
                current = VisualTreeHelper.GetParent(current);
            }
        }
        _pageScrollViewer?.ScrollToVerticalOffset(_pageScrollViewer.VerticalOffset + deltaY);
    }

    private async void OnPreviewNavigationCompleted(object? sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        // 稍等渲染完成後量測內容高度
        await Task.Delay(80);
        try
        {
            var json   = await NotesPreviewWebView.ExecuteScriptAsync("document.documentElement.scrollHeight");
            if (double.TryParse(json, out var px) && px > 0)
                NotesPreviewWebView.Height = Math.Clamp(px + 2, 60, 600);
        }
        catch { /* 若 WebView 已關閉則忽略 */ }
    }

    private void LoadNotesPreview(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            NotesPlaceholder.Visibility = Visibility.Visible;
            NotesPreviewWebView.Visibility = Visibility.Collapsed;
            return;
        }
        NotesPlaceholder.Visibility = Visibility.Collapsed;
        NotesPreviewWebView.Height = 60; // 先收縮，等 NavigationCompleted 再撐開
        NotesPreviewWebView.Visibility = Visibility.Visible;
        if (_previewReady)
            NotesPreviewWebView.NavigateToString(BuildPreviewHtml(html));
    }

    private static string BuildPreviewHtml(string content) => $$"""
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="UTF-8">
          <link href="https://cdn.quilljs.com/1.3.7/quill.snow.css" rel="stylesheet">
          <style>
            * { margin:0; padding:0; box-sizing:border-box; }
            html, body { overflow:hidden; }
            body { font-family:'Microsoft JhengHei UI','Microsoft JhengHei',sans-serif;
                   font-size:14px; background:transparent; }
            .ql-container.ql-snow { border:none; }
            .ql-editor { padding:10px 14px; pointer-events:none; }
            a { pointer-events:auto; }
            @media (prefers-color-scheme:dark) { .ql-editor { color:#d4d4d4; } }
          </style>
        </head>
        <body>
          <div class="ql-snow">
            <div class="ql-container">
              <div class="ql-editor">{{content}}</div>
            </div>
          </div>
          <script>
            document.addEventListener('wheel', e => {
              if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(e.deltaY);
                e.preventDefault();
              }
            }, { passive: false });
          </script>
        </body>
        </html>
        """;

    private async void EditNotes_Click(object sender, RoutedEventArgs e)
    {
        var win = new NotesEditorWindow(_viewModel.Notes) { Owner = Window.GetWindow(this) };
        if (win.ShowDialog() == true)
        {
            _viewModel.Notes = win.SavedHtml;
            LoadNotesPreview(_viewModel.Notes);
        }
    }

    private void AddOwnerBinding_Click(object sender, RoutedEventArgs e)
    {
        var win = App.Services.GetRequiredService<LineFollowerWindow>();
        win.ViewModel.IsSelectMode = true;
        win.ViewModel.PickMode = LineFollowerPickMode.User;
        win.Owner = Window.GetWindow(this);
        win.ViewModel.FollowerSelected += (_, item) =>
        {
            var added = _viewModel.AddOwnerBinding(new OwnerLineBinding
            {
                UserId = item.UserId,
                DisplayName = item.DisplayName,
                PictureUrl = item.PictureUrl
            });
            if (!added)
                App.Services.GetRequiredService<IAppSnackbarService>()
                    .ShowWarning($"「{item.DisplayName}」已經綁定過了");
        };
        win.Loaded += async (_, _) => await win.ViewModel.InitAsync(
            _viewModel.LineChannelAccessToken,
            _viewModel.LineWorkerUrl,
            _viewModel.LineWorkerApiKey);
        win.ShowDialog();
    }

    /// <summary>
    /// 新增群組/多人聊天室綁定：跟業主綁定共用同一個好友清單視窗，只是限制成只挑
    /// TargetType 為 Group/Room 的項目（PickMode），避免把一般好友誤選成群組。
    /// </summary>
    private void AddGroupBinding_Click(object sender, RoutedEventArgs e)
    {
        var win = App.Services.GetRequiredService<LineFollowerWindow>();
        win.ViewModel.IsSelectMode = true;
        win.ViewModel.PickMode = LineFollowerPickMode.GroupOrRoom;
        win.Owner = Window.GetWindow(this);
        win.ViewModel.FollowerSelected += (_, item) =>
        {
            var added = _viewModel.AddGroupBinding(new OwnerLineBinding
            {
                UserId = item.UserId,
                DisplayName = item.DisplayName,
                PictureUrl = item.PictureUrl
            });
            if (!added)
                App.Services.GetRequiredService<IAppSnackbarService>()
                    .ShowWarning($"「{item.DisplayName}」已經綁定過了");
        };
        win.Loaded += async (_, _) => await win.ViewModel.InitAsync(
            _viewModel.LineChannelAccessToken,
            _viewModel.LineWorkerUrl,
            _viewModel.LineWorkerApiKey);
        win.ShowDialog();
    }

    // ── Google 雲端備份／還原 ────────────────────────────────────────────────
    // 兩者都以 GoogleSyncProgressWindow 鎖住操作並顯示進度；還原完成後重啟程式
    // （頁面與 ViewModel 仍持有舊 DB 的資料，熱替換會讀到不一致的狀態）。

    /// <summary>
    /// 進到設定頁時背景比對這個店鋪的雲端備份是否比本機新（多半是在另一台電腦備份過）。
    /// 非同步、不擋畫面：查詢本身有 10 秒逾時，離線/查詢失敗就靜靜略過，不用手動再按一次
    /// 「從雲端還原」也能之後自己修正。
    /// </summary>
    private async Task CheckCloudBackupFreshnessAsync()
    {
        if (!_viewModel.IsGoogleLinked) return;
        try
        {
            var drive = App.Services.GetRequiredService<GoogleDriveSyncService>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var remote = await drive.GetRemoteStatusAsync(cts.Token);
            if (remote is null) return; // 離線、授權失效或雲端沒有備份

            var lastSynced = _viewModel.GoogleDriveLastSyncedRemoteModifiedTime;
            if (lastSynced is not null && remote <= lastSynced) return;

            var dialog = App.Services.GetRequiredService<IAppDialogService>();
            var confirmed = await dialog.ShowConfirmAsync(
                "發現較新的雲端備份",
                $"Google 雲端硬碟上有較新的資料備份（{remote.Value.ToLocalTime():yyyy/MM/dd HH:mm}）。\n\n" +
                "要用雲端資料覆蓋這個店鋪目前的資料嗎？\n" +
                "（覆蓋前會自動保留一份目前資料，完成後程式會重新啟動）",
                "立即還原", "稍後再說");
            if (!confirmed) return;

            // 使用者已經在上面那個對話框確認過了，這裡不再問第二次
            await RunGoogleRestoreAsync();
        }
        catch
        {
            // 背景檢查失敗不用打擾使用者，之後手動按「從雲端還原」一樣可以用
        }
    }

    private async Task RunGoogleBackupAsync()
    {
        var snackbar = App.Services.GetRequiredService<IAppSnackbarService>();
        var drive = App.Services.GetRequiredService<GoogleDriveSyncService>();

        var progress = new GoogleSyncProgressWindow("正在備份到 Google 雲端硬碟…")
        {
            Owner = Window.GetWindow(this),
        };
        progress.Show();
        _viewModel.IsGoogleBusy = true;
        try
        {
            var modifiedTime = await drive.BackupAsync(progress.SetStatus);
            await _viewModel.OnGoogleBackupCompletedAsync(modifiedTime);
            snackbar.ShowSuccess("已備份到 Google 雲端硬碟");
        }
        catch (Exception ex)
        {
            snackbar.ShowError($"備份失敗：{ex.Message}");
        }
        finally
        {
            _viewModel.IsGoogleBusy = false;
            progress.ForceClose();
        }
    }

    /// <summary>使用者主動按「從雲端還原」的入口：先確認，再執行</summary>
    private async Task ConfirmAndRestoreAsync()
    {
        var dialog = App.Services.GetRequiredService<IAppDialogService>();
        var confirmed = await dialog.ShowConfirmAsync(
            "從雲端還原",
            "將用雲端備份覆蓋這個店鋪目前的所有資料（員工、排班、薪資等）。\n\n" +
            "覆蓋前會自動保留一份目前資料的備份，還原後程式會重新啟動。\n\n" +
            "確定要繼續嗎？",
            "確定還原", "取消");
        if (!confirmed) return;

        await RunGoogleRestoreAsync();
    }

    /// <summary>實際執行還原（呼叫端負責先取得使用者確認）</summary>
    private async Task RunGoogleRestoreAsync()
    {
        var snackbar = App.Services.GetRequiredService<IAppSnackbarService>();
        var drive = App.Services.GetRequiredService<GoogleDriveSyncService>();

        var progress = new GoogleSyncProgressWindow("正在從 Google 雲端硬碟還原…")
        {
            Owner = Window.GetWindow(this),
        };
        progress.Show();
        _viewModel.IsGoogleBusy = true;
        try
        {
            await drive.RestoreAsync(progress.SetStatus);
            progress.ForceClose();
            RestartApp();
        }
        catch (Exception ex)
        {
            _viewModel.IsGoogleBusy = false;
            progress.ForceClose();
            snackbar.ShowError($"還原失敗：{ex.Message}");
        }
    }

    /// <summary>重啟程式（沿用軟體更新完成後的做法）</summary>
    private static void RestartApp()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private void OnLineTestSucceeded(object? sender, string token)
    {
        var win = App.Services.GetRequiredService<LineFollowerWindow>();
        win.Owner = Window.GetWindow(this);
        win.Loaded += async (_, _) => await win.ViewModel.InitAsync(
            token, _viewModel.LineWorkerUrl, _viewModel.LineWorkerApiKey);
        win.ShowDialog();
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

    private void ViewFollowers_Click(object sender, RoutedEventArgs e)
    {
        var win = App.Services.GetRequiredService<LineFollowerWindow>();
        win.Owner = Window.GetWindow(this);
        win.Loaded += async (_, _) => await win.ViewModel.InitAsync(
            _viewModel.LineChannelAccessToken,
            _viewModel.LineWorkerUrl,
            _viewModel.LineWorkerApiKey);
        win.ShowDialog();
    }

    // 問題根源：備註預覽使用 WebView2（HwndHost），其 HWND 會吞掉 WM_MOUSEWHEEL，
    // 導致事件無法冒泡到 MainWindow 的 PageScrollViewer。
    // 修法：與 SchedulePage / SalaryPage 一致 —— 在 section Border 的 PreviewMouseWheel
    //        （隧道事件）最先觸發時，向上找到 PageScrollViewer 並直接捲動。
    private ScrollViewer? _pageScrollViewer;

    private void Section_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pageScrollViewer is null)
        {
            var current = VisualTreeHelper.GetParent((DependencyObject)sender);
            while (current is not null)
            {
                if (current is ScrollViewer sv) { _pageScrollViewer = sv; break; }
                current = VisualTreeHelper.GetParent(current);
            }
        }
        if (_pageScrollViewer is null) return;
        e.Handled = true;
        _pageScrollViewer.ScrollToVerticalOffset(_pageScrollViewer.VerticalOffset - e.Delta / 3.0);
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
