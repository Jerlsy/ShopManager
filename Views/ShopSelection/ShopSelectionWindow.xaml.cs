using ShopManager.ViewModels;

namespace ShopManager.Views.ShopSelection;

public partial class ShopSelectionWindow : Wpf.Ui.Controls.FluentWindow
{
    public ShopSelectionWindow(ShopSelectionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.RequestClose += () =>
        {
            DialogResult = true;
            Close();
        };

        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
