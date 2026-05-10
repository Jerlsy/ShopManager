using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ShopManager.Models;
using ShopManager.ViewModels;
using ShopManager.Views.Line;
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
            await InitNotesPreviewAsync();
        };
        viewModel.LineTestSucceeded += OnLineTestSucceeded;
    }

    private bool _previewReady;

    private async Task InitNotesPreviewAsync()
    {
        await NotesPreviewWebView.EnsureCoreWebView2Async();
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

    private async void AddOwnerBinding_Click(object sender, RoutedEventArgs e)
    {
        var win = App.Services.GetRequiredService<LineFollowerWindow>();
        win.ViewModel.IsSelectMode = true;
        win.Owner = Window.GetWindow(this);
        win.ViewModel.FollowerSelected += (_, item) =>
            _viewModel.AddOwnerBinding(new OwnerLineBinding
            {
                UserId = item.UserId,
                DisplayName = item.DisplayName,
                PictureUrl = item.PictureUrl
            });
        win.Show();
        await win.ViewModel.InitAsync(
            _viewModel.LineChannelAccessToken,
            _viewModel.LineWorkerUrl,
            _viewModel.LineWorkerApiKey);
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
