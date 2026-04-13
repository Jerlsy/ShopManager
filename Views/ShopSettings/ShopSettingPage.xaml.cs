using ShopManager.ViewModels;
using System.Windows.Controls;

namespace ShopManager.Views.ShopSettings;

public partial class ShopSettingPage : UserControl
{
    public ShopSettingPage(SystemSettingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
