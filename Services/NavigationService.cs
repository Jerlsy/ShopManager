using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace ShopManager.Services;

public partial class NavigationService(IServiceProvider sp) : ObservableObject
{
    private readonly Dictionary<Type, object> _cache = new();

    [ObservableProperty] private object? _currentContent;

    public void Navigate(Type pageType)
    {
        if (!_cache.TryGetValue(pageType, out var page))
        {
            page = sp.GetRequiredService(pageType);
            _cache[pageType] = page;
        }
        CurrentContent = page;
    }

    /// <summary>清除頁面快取（店鋪切換後需重置）</summary>
    public void ClearCache() => _cache.Clear();
}
