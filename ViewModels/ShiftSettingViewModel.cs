using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShopManager.Models;
using ShopManager.Services;

namespace ShopManager.ViewModels;

public record ShiftSettingChangedMessage(int TotalConflictCount = 0);

public partial class ShiftSettingViewModel(
    ShiftSettingService service,
    ScheduleConflictService conflictService,
    IAppSnackbarService snackbarService,
    IAppDialogService dialogService) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    private List<ShiftSetting> _shifts = new();

    [ObservableProperty] private ShiftSetting? _selectedShift;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    private bool _isEditing;

    public bool ShowEmptyHint => !IsEditing && Shifts.Count == 0;

    // 編輯用暫存欄位
    [ObservableProperty] private string _editAlias = string.Empty;
    [ObservableProperty] private bool _editIsEnabled = true;
    [ObservableProperty] private string _editColor = "#E53935";

    // 預定義色票（色相均勻分布，每色相僅一個代表色）
    public static List<string> ColorPalette { get; } = new()
    {
        "#E53935", // 紅
        "#FF7043", // 珊瑚橙
        "#FB8C00", // 橙
        "#F9A825", // 金黃
        "#AFB42B", // 黃綠
        "#7CB342", // 草綠
        "#43A047", // 深綠
        "#00897B", // 翡翠
        "#00ACC1", // 青
        "#039BE5", // 天藍
        "#1E88E5", // 藍
        "#3949AB", // 靛藍
        "#6A1B9A", // 深紫
        "#8E24AA", // 紫
        "#D81B60", // 桃紅
        "#F06292", // 粉紅
        "#558B2F", // 橄欖綠
        "#00695C", // 深翡翠
        "#4E342E", // 棕
        "#546E7A", // 藍灰
    };

    // 時間欄位：用字串雙向綁定，儲存時轉 TimeOnly
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartTimeError))]
    private string _editStartTimeText = "09:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndTimeError))]
    private string _editEndTimeText = "18:00";

    // 驗證訊息
    public string? StartTimeError =>
        TryParseTime(EditStartTimeText, out _) ? null : "格式錯誤，請輸入 HH:mm（例：09:00）";

    public string? EndTimeError =>
        TryParseTime(EditEndTimeText, out _) ? null : "格式錯誤，請輸入 HH:mm（例：18:00）";

    public bool HasTimeErrors =>
        StartTimeError is not null || EndTimeError is not null;

    // 計算工時（顯示用）
    public string WorkHoursDisplay
    {
        get
        {
            if (!TryParseTime(EditStartTimeText, out var s) || !TryParseTime(EditEndTimeText, out var e))
                return "--";
            var hours = e > s ? (e - s).TotalHours : (e.AddHours(24) - s).TotalHours;
            return $"{hours:F1} 小時{(e < s ? "（跨日）" : "")}";
        }
    }

    // 快速選擇常用時間清單
    public static List<string> CommonTimes { get; } = new()
    {
        "06:00", "07:00", "08:00", "09:00", "10:00", "11:00",
        "12:00", "13:00", "14:00", "15:00", "16:00", "17:00",
        "18:00", "19:00", "20:00", "21:00", "22:00", "23:00", "00:00"
    };

    // 通知工時顯示更新
    partial void OnEditStartTimeTextChanged(string value) => OnPropertyChanged(nameof(WorkHoursDisplay));
    partial void OnEditEndTimeTextChanged(string value) => OnPropertyChanged(nameof(WorkHoursDisplay));

    public async Task LoadAsync()
    {
        Shifts = (await service.GetAllAsync())
            .OrderBy(s => s.Alias)
            .ThenBy(s => s.StartTime)
            .ToList();
    }

    [RelayCommand]
    public void StartNew()
    {
        SelectedShift = null;
        EditAlias = string.Empty;
        EditStartTimeText = "09:00";
        EditEndTimeText = "18:00";
        EditIsEnabled = true;
        EditColor = "#4A90D9";
        IsEditing = true;
    }

    [RelayCommand]
    public void StartEdit(ShiftSetting shift)
    {
        SelectedShift = shift;
        EditAlias = shift.Alias;
        EditStartTimeText = shift.StartTime.ToString("HH:mm");
        EditEndTimeText = shift.EndTime.ToString("HH:mm");
        EditIsEnabled = shift.IsEnabled;
        EditColor = shift.Color;
        IsEditing = true;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!TryParseTime(EditStartTimeText, out var startTime) ||
            !TryParseTime(EditEndTimeText, out var endTime))
            return;

        int? updatedShiftId = null;
        if (SelectedShift is null)
        {
            await service.AddAsync(new ShiftSetting
            {
                Alias = EditAlias,
                StartTime = startTime,
                EndTime = endTime,
                IsEnabled = EditIsEnabled,
                Color = EditColor
            });
        }
        else
        {
            updatedShiftId = SelectedShift.Id;
            SelectedShift.Alias = EditAlias;
            SelectedShift.StartTime = startTime;
            SelectedShift.EndTime = endTime;
            SelectedShift.IsEnabled = EditIsEnabled;
            SelectedShift.Color = EditColor;
            await service.UpdateAsync(SelectedShift);
        }
        IsEditing = false;
        await LoadAsync();
        snackbarService.ShowSuccess("班別設定已儲存");

        // 班別時間/設定變更 → 先 recheck，再發消息（確保排班頁讀到最新衝突數）
        int totalConflicts = 0;
        if (updatedShiftId.HasValue)
        {
            totalConflicts = await conflictService.RecheckByShiftAsync(updatedShiftId.Value);
            if (totalConflicts > 0)
                snackbarService.ShowWarning($"儲存後發現 {totalConflicts} 條排班衝突");
        }
        WeakReferenceMessenger.Default.Send(new ShiftSettingChangedMessage(totalConflicts));
    }

    [RelayCommand]
    public async Task DeleteAsync(ShiftSetting shift)
    {
        var entryCount = await service.GetEntryCountAsync(shift.Id);

        var message = entryCount > 0
            ? $"確定要刪除班別「{shift.Alias}」嗎？此操作無法復原。\n\n⚠️ 此班別有 {entryCount} 筆排班記錄，刪除後將一併移除。"
            : $"確定要刪除班別「{shift.Alias}」嗎？此操作無法復原。";

        var confirmed = await dialogService.ShowConfirmAsync("確認刪除", message, "刪除", "取消");
        if (!confirmed) return;

        // 刪除前先記錄受影響的班表 ID（cascade 刪除後 entries 已不存在）
        var affectedIds = await service.GetAffectedScheduleIdsAsync(shift.Id);

        await service.DeleteAsync(shift.Id);
        await LoadAsync();

        // 清除孤立衝突記錄，並對受影響班表重新評估
        int totalConflicts = 0;
        foreach (var scheduleId in affectedIds)
            totalConflicts += await conflictService.RecheckAsync(scheduleId);
        WeakReferenceMessenger.Default.Send(new ShiftSettingChangedMessage(totalConflicts));
    }

    [RelayCommand]
    public void Cancel() => IsEditing = false;

    // ── 工具方法 ─────────────────────────────────────────
    private static bool TryParseTime(string text, out TimeOnly result)
    {
        if (TimeOnly.TryParseExact(text?.Trim() ?? "", "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result))
            return true;
        result = default;
        return false;
    }
}
