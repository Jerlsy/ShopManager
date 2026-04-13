using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public partial class ShopSelectionViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly ShopContext _shopContext;

    // 哨兵值：代表「新增店鋪」選項
    public static readonly Shop NewShopSentinel = new() { Id = Guid.Empty, Name = "＋ 新增店鋪..." };

    public ObservableCollection<Shop> Shops { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewShopSelected))]
    [NotifyCanExecuteChangedFor(nameof(EnterCommand))]
    private Shop? _selectedShop;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnterCommand))]
    private string _newShopName = string.Empty;

    public bool IsNewShopSelected => SelectedShop?.Id == Guid.Empty;

    public event Action? RequestClose;

    public ShopSelectionViewModel(AppDbContext db, ShopContext shopContext)
    {
        _db = db;
        _shopContext = shopContext;
    }

    public async Task LoadAsync()
    {
        var shops = await _db.Shops.OrderBy(s => s.Name).ToListAsync();
        Shops.Clear();
        foreach (var shop in shops)
            Shops.Add(shop);

        // 最後加入「新增店鋪」哨兵選項
        Shops.Add(NewShopSentinel);

        SelectedShop = Shops.Count > 1 ? Shops[0] : NewShopSentinel;
    }

    private bool CanEnter =>
        SelectedShop is not null &&
        (!IsNewShopSelected || !string.IsNullOrWhiteSpace(NewShopName));

    [RelayCommand(CanExecute = nameof(CanEnter))]
    private async Task EnterAsync()
    {
        if (IsNewShopSelected)
        {
            // 建立新店鋪
            var trimmed = NewShopName.Trim();
            var shop = new Shop { Name = trimmed };
            _db.Shops.Add(shop);

            // 同時建立對應的 ShopSetting，帶入店名
            _db.ShopSettings.Add(new ShopSetting
            {
                ShopId = shop.Id,
                Name = trimmed,
            });

            await _db.SaveChangesAsync();

            _shopContext.ShopId = shop.Id;
            _shopContext.ShopName = shop.Name;
        }
        else
        {
            _shopContext.ShopId = SelectedShop!.Id;
            _shopContext.ShopName = SelectedShop!.Name;
        }

        RequestClose?.Invoke();
    }
}
