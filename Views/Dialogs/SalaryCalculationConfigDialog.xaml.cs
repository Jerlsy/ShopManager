using MaterialDesignThemes.Wpf;
using ShopManager.ViewModels;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public partial class SalaryCalculationConfigDialog : UserControl
{
    private readonly SalaryCalculationConfigViewModel _vm;

    public SalaryCalculationConfigDialog(SalaryCalculationConfigViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
        => DialogHost.CloseDialogCommand.Execute(_vm.BuildConfig(), this);
}
