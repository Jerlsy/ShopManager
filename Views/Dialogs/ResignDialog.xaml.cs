using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public record ResignDialogResult(DateOnly ResignDate, bool SendLineMessage);

public partial class ResignDialog : UserControl
{
    /// <param name="linePreview">null＝不能傳 LINE（未綁定或未設定 LINE），不顯示勾選</param>
    public ResignDialog(string employeeName, string? linePreview)
    {
        InitializeComponent();
        TitleText.Text = $"設定離職：{employeeName}";
        ResignDatePicker.SelectedDate = DateTime.Today;

        if (linePreview is not null)
        {
            LinePanel.Visibility = Visibility.Visible;
            LinePreviewText.Text = linePreview;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResignDatePicker.SelectedDate is not DateTime date)
        {
            DateError.Visibility = Visibility.Visible;
            return;
        }

        var sendLine = LinePanel.Visibility == Visibility.Visible && SendLineCheckBox.IsChecked == true;
        DialogHost.CloseDialogCommand.Execute(new ResignDialogResult(DateOnly.FromDateTime(date), sendLine), this);
    }
}
