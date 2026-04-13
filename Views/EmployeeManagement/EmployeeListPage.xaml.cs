using ShopManager.ViewModels;
using System.Windows.Controls;

namespace ShopManager.Views.EmployeeManagement;

public partial class EmployeeListPage : UserControl
{
    public EmployeeListPage(EmployeeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
