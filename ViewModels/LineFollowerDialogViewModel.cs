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
    LineTargetType TargetType,
    int? BoundEmployeeId,
    string? BoundEmployeeName,
    bool IsBindingDisabled)
{
    public bool IsBound => BoundEmployeeId.HasValue;
    public bool IsActiveBinding => IsBound && !IsBindingDisabled;
    public bool IsDisabledBinding => IsBound && IsBindingDisabled;
    public bool IsUnbound => !IsBound;
    public bool IsGroupOrRoom => TargetType is LineTargetType.Group or LineTargetType.Room;
}

/// <summary>選取模式下要挑哪一種目標——個人好友（綁員工/業主）或群組/多人聊天室（綁群組推播）</summary>
public enum LineFollowerPickMode { User, GroupOrRoom }

public partial class LineFollowerDialogViewModel(
    LineFollowerService followerService,
    AppDbContext db,
    ShopContext shopContext) : ObservableObject
{
    private string _token = string.Empty;
    private string _workerUrl = string.Empty;
    private string _apiKey = string.Empty;

    /// <summary>true = 從員工頁/設定頁開啟，顯示「選擇」按鈕</summary>
    public bool IsSelectMode { get; set; }

    /// <summary>
    /// 只在 IsSelectMode 時生效：限制清單只顯示這個種類的目標，避免例如把群組誤選為業主。
    /// 純檢視模式（查看好友清單）不受此限制，全部都顯示。
    /// </summary>
    public LineFollowerPickMode PickMode { get; set; } = LineFollowerPickMode.User;

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

            var filtered = IsSelectMode
                ? rawList.Where(f => PickMode == LineFollowerPickMode.User
                    ? f.TargetType == LineTargetType.User
                    : f.TargetType is LineTargetType.Group or LineTargetType.Room)
                : rawList;

            Followers.Clear();
            foreach (var f in filtered)
            {
                var empName = f.BoundEmployeeId.HasValue && empMap.TryGetValue(f.BoundEmployeeId.Value, out var emp)
                    ? emp.Name : null;
                Followers.Add(new LineFollowerItem(
                    f.Id, f.UserId, f.DisplayName, f.PictureUrl, f.TargetType,
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
