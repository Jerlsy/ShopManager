using Microsoft.Win32;
using ShopManager.ViewModels;
using System.Windows;
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

    private void PickLogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "圖片檔案|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
            Title = "選擇店鋪 Logo"
        };
        if (dlg.ShowDialog() != true) return;

        var cropWin = new LogoCropWindow(dlg.FileName)
        {
            Owner = Window.GetWindow(this)
        };
        if (cropWin.ShowDialog() != true || cropWin.CroppedPng == null) return;

        ((SystemSettingViewModel)DataContext).SetLogoPhoto(cropWin.CroppedPng);
    }
}
