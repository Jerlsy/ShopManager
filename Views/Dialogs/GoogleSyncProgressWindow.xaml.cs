using System.ComponentModel;
using System.Windows;

namespace ShopManager.Views.Dialogs;

/// <summary>
/// Google 雲端同步進度視窗。以 Show() 開啟並在期間鎖住呼叫端視窗，同步結束由呼叫端 Close()。
/// 由「設定頁手動備份／還原」與「App 啟動時自動偵測」兩處共用。
/// </summary>
public partial class GoogleSyncProgressWindow : Window
{
    private bool _allowClose;

    public GoogleSyncProgressWindow(string header)
    {
        InitializeComponent();
        HeaderText.Text = header;
    }

    /// <summary>更新狀態文字（可從背景執行緒呼叫）</summary>
    public void SetStatus(string message) =>
        Dispatcher.Invoke(() => StatusText.Text = message);

    /// <summary>由呼叫端在同步結束後改用此方法關閉（避免使用者中途關掉視窗）</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 同步中不可關閉：中斷上傳會讓雲端留下不完整的備份、中斷還原會讓本機 DB 處於半替換狀態
        if (!_allowClose) e.Cancel = true;
        base.OnClosing(e);
    }
}
