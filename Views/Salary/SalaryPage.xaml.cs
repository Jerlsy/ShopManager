using ShopManager.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShopManager.Views.Salary;

public partial class SalaryPage : UserControl
{
    private readonly SalaryViewModel _vm;

    public SalaryPage(SalaryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _vm.OpenPayrollRecordRequested += (_, data) =>
        {
            var win = new PayrollRecordWindow(data) { Owner = Window.GetWindow(this) };
            win.ShowDialog();
        };
    }

    private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await _vm.LoadAsync();
    }

    // 問題根源：員工卡片內的 ComboBox、TextBox 等子元素攔截 MouseWheel 事件，
    // 導致事件無法冒泡到 PageScrollViewer（背景捲動正常的原因）。
    // 修法：在員工卡片 Border 的 PreviewMouseWheel（隧道事件）最先觸發時，
    //        向上走訪找到 PageScrollViewer 並直接捲動，複製游標在背景時的行為。
    private ScrollViewer? _pageScrollViewer;

    private void EmployeeCard_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pageScrollViewer is null)
        {
            var current = VisualTreeHelper.GetParent((DependencyObject)sender);
            while (current is not null)
            {
                if (current is ScrollViewer sv) { _pageScrollViewer = sv; break; }
                current = VisualTreeHelper.GetParent(current);
            }
        }
        if (_pageScrollViewer is null) return;
        e.Handled = true;
        _pageScrollViewer.ScrollToVerticalOffset(_pageScrollViewer.VerticalOffset - e.Delta / 3.0);
    }
}
