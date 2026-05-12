using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

public record LineFollowerItem(
    int Id,
    string UserId,
    string DisplayName,
    string? PictureUrl,
    int? BoundEmployeeId,
    string? BoundEmployeeName,
    bool IsBindingDisabled)
{
    public bool IsBound => BoundEmployeeId.HasValue;
    public bool IsActiveBinding => IsBound && !IsBindingDisabled;
    public bool IsDisabledBinding => IsBound && IsBindingDisabled;
    public bool IsUnbound => !IsBound;
}

public partial class LineFollowerDialogViewModel(
    LineFollowerService followerService,
    AppDbContext db,
    ShopContext shopContext) : ObservableObject
{
    private string _token = string.Empty;
    private string _workerUrl = string.Empty;
    private string _apiKey = string.Empty;

    /// <summary>true = 從員工頁開啟，顯示「選擇」按鈕</summary>
    public bool IsSelectMode { get; set; }

    [ObservableProperty] private ObservableCollection<LineFollowerItem> _followers = new();
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _lastSyncText = "尚未同步";

    /// <summary>Select 模式下選取後觸發，回傳選取的 item</summary>
    public event EventHandler<LineFollowerItem>? FollowerSelected;

    public async Task InitAsync(string token, string workerUrl, string apiKey)
    {
        _token = token;
        _workerUrl = workerUrl;
        _apiKey = apiKey;
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_workerUrl) || string.IsNullOrWhiteSpace(_apiKey)) return;
        IsBusy = true;
        HasError = false;
        ErrorMessage = null;
        try
        {
            var rawList = await followerService.SyncAndGetAllAsync(_workerUrl, _apiKey, _token);
            var employees = await db.Employees
                .Where(e => e.ShopId == shopContext.ShopId)
                .ToListAsync();
            var empMap = employees.ToDictionary(e => e.Id);

            Followers.Clear();
            foreach (var f in rawList)
            {
                var empName = f.BoundEmployeeId.HasValue && empMap.TryGetValue(f.BoundEmployeeId.Value, out var emp)
                    ? emp.Name : null;
                Followers.Add(new LineFollowerItem(
                    f.Id, f.UserId, f.DisplayName, f.PictureUrl,
                    f.BoundEmployeeId, empName, f.IsBindingDisabled));
            }
            LastSyncText = $"最後同步：{DateTime.Now:MM/dd HH:mm}";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    public void SelectFollower(LineFollowerItem item)
    {
        FollowerSelected?.Invoke(this, item);
    }
}
