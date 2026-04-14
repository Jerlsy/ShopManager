using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView(string title, string content, string confirmText, string cancelText)
    {
        InitializeComponent();
        TitleText.Text = title;
        ContentText.Text = content;
        ConfirmBtn.Content = confirmText;
        CancelBtn.Content = cancelText;
    }
}
