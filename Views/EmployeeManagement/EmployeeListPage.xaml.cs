using Microsoft.Win32;
using ShopManager.Models;
using ShopManager.ViewModels;
using System.Windows;
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

    private async void EmployeeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not Employee emp) return;
        var vm = (EmployeeViewModel)DataContext;
        await vm.StartEditAsync(emp);
    }

    private void PickAvatar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "圖片檔案|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
            Title = "選擇大頭貼照片"
        };
        if (dlg.ShowDialog() != true) return;

        var cropWin = new AvatarCropWindow(dlg.FileName)
        {
            Owner = Window.GetWindow(this)
        };
        if (cropWin.ShowDialog() != true || cropWin.CroppedPng == null) return;

        var vm = (EmployeeViewModel)DataContext;
        vm.SetAvatarPhoto(cropWin.CroppedPng);
    }
}
