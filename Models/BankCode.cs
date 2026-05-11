namespace ShopManager.Models;

public record BankCode(string Code, string Name)
{
    public string DisplayLabel => $"{Code} {Name}";
}
