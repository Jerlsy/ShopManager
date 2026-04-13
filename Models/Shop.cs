using System.ComponentModel.DataAnnotations;

namespace ShopManager.Models;

/// <summary>店鋪實體，做為所有資料的頂層 key</summary>
public class Shop
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public string Name { get; set; } = string.Empty;
}
