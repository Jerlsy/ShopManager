using ShopManager.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace ShopManager.Views.Line;

public partial class LineFollowerWindow : Window
{
    public LineFollowerDialogViewModel ViewModel { get; }

    public LineFollowerWindow(LineFollowerDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LineFollowerItem item })
        {
            ViewModel.SelectFollower(item);
            Close();
        }
    }
}
