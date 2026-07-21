using System.Windows;

namespace ShopManager.Views;

public partial class UpdateDownloadWindow : Window
{
    public UpdateDownloadWindow()
    {
        InitializeComponent();
    }

    /// <summary>更新下載進度；totalBytes 為 null（伺服器未回傳 Content-Length）時顯示不確定進度。</summary>
    public void SetProgress(long bytesReceived, long? totalBytes)
    {
        double mb = bytesReceived / 1024.0 / 1024.0;
        if (totalBytes is > 0)
        {
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Maximum = totalBytes.Value;
            DownloadProgressBar.Value = bytesReceived;
            DownloadProgressText.Text = $"{mb:F1} MB / {totalBytes.Value / 1024.0 / 1024.0:F1} MB";
        }
        else
        {
            DownloadProgressBar.IsIndeterminate = true;
            DownloadProgressText.Text = $"已下載 {mb:F1} MB…";
        }
    }
}
