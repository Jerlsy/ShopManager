using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public partial class ChoiceDialogView : UserControl
{
    public ChoiceDialogView(string title, string content, string primaryText, string secondaryText, string cancelText)
    {
        InitializeComponent();
        TitleText.Text       = title;
        ContentText.Text     = content;
        PrimaryBtn.Content   = primaryText;
        SecondaryBtn.Content = secondaryText;
        CancelBtn.Content    = cancelText;
    }
}
