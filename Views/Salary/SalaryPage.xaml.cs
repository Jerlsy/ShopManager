using ShopManager.ViewModels;
using System.Windows.Controls;

namespace ShopManager.Views.Salary;

public partial class SalaryPage : UserControl
{
    private readonly SalaryViewModel _vm;

    public SalaryPage(SalaryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.LoadAsync();
    }
}
