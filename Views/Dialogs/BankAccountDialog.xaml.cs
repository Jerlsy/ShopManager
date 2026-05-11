using MaterialDesignThemes.Wpf;
using ShopManager.Helpers;
using ShopManager.Models;
using System.Collections.Generic;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public record BankAccountDialogResult(BankCode? Bank, string Account, string AccountName);

public partial class BankAccountDialog : UserControl
{
    private readonly List<BankCode> _bankCodes;

    public BankAccountDialog(List<BankCode> bankCodes, BankCode? selectedBank, string account, string accountName)
    {
        InitializeComponent();

        _bankCodes = bankCodes;
        BankComboBox.ItemsSource = bankCodes;
        BankComboBox.SelectedItem = selectedBank;
        AccountBox.Text = account;
        AccountNameBox.Text = accountName;
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var bank = BankComboBox.SelectedItem as BankCode;
        var account = AccountBox.Text?.Replace("-", "").Replace(" ", "") ?? string.Empty;
        var accountName = AccountNameBox.Text?.Trim() ?? string.Empty;

        // 有填帳號但沒選銀行，或有選銀行但沒填帳號
        if (!string.IsNullOrWhiteSpace(account) && bank is null)
        {
            AccountError.Text = "請選擇銀行";
            AccountError.Visibility = System.Windows.Visibility.Visible;
            return;
        }
        if (bank is not null && string.IsNullOrWhiteSpace(account))
        {
            AccountError.Text = "請輸入帳號";
            AccountError.Visibility = System.Windows.Visibility.Visible;
            return;
        }
        if (!string.IsNullOrWhiteSpace(account) && !BankAccountValidator.Validate(account))
        {
            AccountError.Text = "帳號格式不正確（10–16 位數字）";
            AccountError.Visibility = System.Windows.Visibility.Visible;
            return;
        }

        AccountError.Visibility = System.Windows.Visibility.Collapsed;
        DialogHost.CloseDialogCommand.Execute(
            new BankAccountDialogResult(bank, account, accountName), this);
    }
}
