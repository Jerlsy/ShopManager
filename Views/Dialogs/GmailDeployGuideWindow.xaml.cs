using ShopManager.Services;
using System.Windows;

namespace ShopManager.Views.Dialogs;

/// <summary>顯示Gmail轉Line推播的 Apps Script 部署步驟與完整程式碼，File ID 依當前店鋪動態帶入</summary>
public partial class GmailDeployGuideWindow : Window
{
    public GmailDeployGuideWindow(string configFileId)
    {
        InitializeComponent();
        ConfigFileIdText.Text = string.IsNullOrEmpty(configFileId)
            ? "（尚未取得，請先成功上傳一次規則）"
            : configFileId;
        CodeText.Text = GmailAppsScriptTemplate.SourceCode;
    }

    private void CopyFileId_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(ConfigFileIdText.Text);
    private void CopyCode_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(CodeText.Text);
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
