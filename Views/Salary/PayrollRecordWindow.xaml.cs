using ShopManager.Models;
using ShopManager.Services;
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

        async Task SendSalarySlipAsync(DateTime? paidAt)
        {
            var png = SalarySlipImageRenderer.RenderPng(r, data.Record.Year, data.Record.Month, paidAt, data.ShopName);
            var success = await data.SendLineImage(r.Employee.LineUserId!, png);
            if (!success)
                MessageBox.Show("LINE 推播失敗，請確認 Channel Access Token 與 Worker 設定。", "推播失敗",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        entry.OnIsPaidToggled = async paid =>
        {
            await data.UpdatePaymentStatus(r.Id, paid);

            // 只有「勾選為已支薪」且該員工有綁定 LINE 時才詢問，取消勾選不觸發
            if (paid && entry.HasLineBinding)
            {
                var ask = MessageBox.Show(
                    $"「{r.Employee.Name}」已標記為已支薪，是否要推播 LINE 通知薪資已入帳？",
                    "推播入帳通知", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ask == MessageBoxResult.Yes)
                    await SendSalarySlipAsync(DateTime.Now);
            }
        };

        entry.OnSendLine = () => SendSalarySlipAsync(entry.IsPaid ? entry.PaidAt : null);

        return entry;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
