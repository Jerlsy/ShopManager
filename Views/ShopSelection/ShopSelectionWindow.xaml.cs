using ShopManager.ViewModels;
using System.Windows;

namespace ShopManager.Views.ShopSelection;

public partial class ShopSelectionWindow : Window
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
