using ShopManager.Models;
using System.Windows;

namespace ShopManager.Views.Salary;

/// <summary>
/// 「每日出勤明細」彈出視窗：以獨立視窗顯示，而非塞在薪資卡片內展開——
/// 明細內容偏長（整月每一天），跟卡片本身其他欄位搶版面，彈窗看起來乾淨也方便捲動比對。
/// </summary>
public partial class SalaryDailyDetailWindow : Window
{
    public SalaryDailyDetailWindow(string employeeName, IReadOnlyList<SalaryDailyEntry> entries)
    {
        InitializeComponent();
        DataContext = new SalaryDailyDetailViewData(employeeName, entries);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public class SalaryDailyDetailViewData
{
    public string HeaderTitle { get; }
    public IReadOnlyList<SalaryDailyEntry> Entries { get; }
    public bool HasEntries => Entries.Count > 0;

    public SalaryDailyDetailViewData(string employeeName, IReadOnlyList<SalaryDailyEntry> entries)
    {
        HeaderTitle = $"{employeeName} 每日出勤明細";
        Entries = entries;
    }
}
