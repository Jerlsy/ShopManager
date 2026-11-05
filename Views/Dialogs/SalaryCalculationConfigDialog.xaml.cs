using MaterialDesignThemes.Wpf;
using ShopManager.ViewModels;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public partial class SalaryCalculationConfigDialog : UserControl
{
    private readonly SalaryCalculationConfigViewModel _vm;

    public SalaryCalculationConfigDialog(SalaryCalculationConfigViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_vm.Validate()) return;   // 缺薪資／假日薪資設定：訊息顯示在按鈕上方，對話框不關閉
        if (!_vm.PersistAttendanceFile()) return;   // 打卡檔保存／刪除失敗：同樣顯示在按鈕上方
        DialogHost.CloseDialogCommand.Execute(_vm.BuildConfig(), this);
    }
}
