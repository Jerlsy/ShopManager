using ShopManager.Models;
using ShopManager.ViewModels;
using System.Windows;

namespace ShopManager.Views.Salary;

public partial class PayrollRecordWindow : Window
{
    private readonly PayrollRecordWindowData _data;

    public PayrollRecordWindow(PayrollRecordWindowData data)
    {
        InitializeComponent();
        _data = data;

        var record = data.Record;
        TitleText.Text = $"{record.Year}年{record.Month}月 發薪紀錄";

        var entries = record.EmployeeRecords
            .Where(r => r.Employee is not null)
            .Select(r => CreateEntry(r, data))
            .ToList();

        EntryList.ItemsSource = entries;
    }

    private static PayrollEntryItem CreateEntry(SalaryEmployeeRecord r, PayrollRecordWindowData data)
    {
        var bankCode = data.BankCodes.FirstOrDefault(b => b.Code == r.Employee.BankCode);
        var bankSummary = (bankCode is not null && !string.IsNullOrEmpty(r.Employee.BankAccount))
            ? $"{bankCode.DisplayLabel}  ****{r.Employee.BankAccount[^Math.Min(4, r.Employee.BankAccount.Length)..]}"
            : "未設定銀行帳戶";
        if (!string.IsNullOrEmpty(r.Employee.BankAccountName))
            bankSummary += $"　戶名：{r.Employee.BankAccountName}";

        var grandTotal = r.BaseAmount + r.BonusItems.Sum(b => b.Amount);

        var entry = new PayrollEntryItem
        {
            RecordId       = r.Id,
            Employee       = r.Employee,
            GrandTotal     = grandTotal,
            BankSummary    = bankSummary,
            HasLineBinding = !string.IsNullOrEmpty(r.Employee.LineUserId),
            EmpRecord      = r,
            Year           = data.Record.Year,
            Month          = data.Record.Month,
        };

        entry.SetInitialStatus(r.IsPaid, r.PaidAt);

        entry.OnIsPaidToggled = paid => data.UpdatePaymentStatus(r.Id, paid);

        entry.OnSendLine = async () =>
        {
            var msg = PayrollEntryItem.BuildSalarySlip(r, data.Record.Year, data.Record.Month,
                entry.IsPaid ? entry.PaidAt : null);
            var success = await data.SendLineMessage(r.Employee.LineUserId!, msg);
            if (!success)
                MessageBox.Show("LINE 推播失敗，請確認 Channel Access Token 設定。", "推播失敗",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        return entry;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
