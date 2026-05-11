namespace ShopManager.Helpers;

public static class BankAccountValidator
{
    public static bool Validate(string? account)
    {
        if (string.IsNullOrWhiteSpace(account)) return false;
        var digits = account.Replace("-", "").Replace(" ", "");
        if (digits.Length < 10 || digits.Length > 16) return false;
        return digits.All(char.IsDigit);
    }
}
