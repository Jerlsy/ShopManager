using MaterialDesignThemes.Wpf;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public record EmploymentDialogResult(DateOnly? InterviewDate, DateOnly HireDate, DateOnly? ResignDate);

public partial class EmploymentDialog : UserControl
{
    public EmploymentDialog(DateOnly? interviewDate, DateOnly hireDate, DateOnly? resignDate)
    {
        InitializeComponent();

        if (interviewDate.HasValue)
            InterviewDatePicker.SelectedDate = new DateTime(interviewDate.Value.Year, interviewDate.Value.Month, interviewDate.Value.Day);

        HireDatePicker.SelectedDate = hireDate != default
            ? new DateTime(hireDate.Year, hireDate.Month, hireDate.Day)
            : DateTime.Today;

        if (resignDate.HasValue)
            ResignDatePicker.SelectedDate = new DateTime(resignDate.Value.Year, resignDate.Value.Month, resignDate.Value.Day);
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (HireDatePicker.SelectedDate is not DateTime hireDateTime)
        {
            HireDateError.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        var result = new EmploymentDialogResult(
            InterviewDate: InterviewDatePicker.SelectedDate is DateTime id ? DateOnly.FromDateTime(id) : null,
            HireDate: DateOnly.FromDateTime(hireDateTime),
            ResignDate: ResignDatePicker.SelectedDate is DateTime rd ? DateOnly.FromDateTime(rd) : null);

        DialogHost.CloseDialogCommand.Execute(result, this);
    }
}
