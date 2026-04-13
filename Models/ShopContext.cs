namespace ShopManager.Models;

/// <summary>Singleton，保存當前選擇的店鋪資訊</summary>
public class ShopContext
{
    public Guid ShopId { get; set; }
    public string ShopName { get; set; } = string.Empty;
}
