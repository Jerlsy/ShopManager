using System.Diagnostics;
using System.Windows;

namespace ShopManager.Views.Line;

public partial class LineTokenHelpWindow : Window
{
    public LineTokenHelpWindow()
    {
        InitializeComponent();
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://developers.line.biz/console/") { UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
