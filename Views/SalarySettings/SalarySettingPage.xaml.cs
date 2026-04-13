using ShopManager.ViewModels;
using System.Windows.Controls;

namespace ShopManager.Views.SalarySettings;

public partial class SalarySettingPage : UserControl
{
    public SalarySettingPage(SalarySettingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
