using ShopManager.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShopManager.Views.ShiftSettings;

public partial class ShiftSettingPage : UserControl
{
    public ShiftSettingPage(ShiftSettingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }

    private void ColorSwatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe
            && fe.DataContext is string color
            && DataContext is ShiftSettingViewModel vm)
        {
            vm.EditColor = color;
        }
    }
}
