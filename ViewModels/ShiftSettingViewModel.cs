using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;

namespace ShopManager.ViewModels;

public partial class ShiftSettingViewModel(
    ShiftSettingService service,
    IAppSnackbarService snackbarService,
    IAppDialogService dialogService) : ObservableObject
{
    [ObservableProperty] private List<ShiftSetting> _shifts = new();
    [ObservableProperty] private ShiftSetting? _selectedShift;
    [ObservableProperty] private bool _isEditing;

    // 編輯用暫存欄位
    [ObservableProperty] private string _editAlias = string.Empty;
    [ObservableProperty] private bool _editIsEnabled = true;
    [ObservableProperty] private string _editColor = "#4A90D9";

    // 預定義色票
    public static List<string> ColorPalette { get; } = new()
    {
        "#4A90D9", "#50C878", "#FF6B6B", "#FFB347", "#9B59B6",
        "#1ABC9C", "#E74C3C", "#F39C12", "#3498DB", "#2ECC71",
        "#E67E22", "#9B27AF", "#00BCD4", "#FF5722", "#607D8B",
        "#795548", "#FF9800", "#8BC34A", "#03A9F4", "#673AB7",
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
        Shifts = await service.GetAllAsync();
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
    }

    [RelayCommand]
    public async Task DeleteAsync(ShiftSetting shift)
    {
        var confirmed = await dialogService.ShowConfirmAsync(
            "確認刪除",
            $"確定要刪除班別「{shift.Alias}」嗎？此操作無法復原。",
            "刪除", "取消");

        if (!confirmed) return;

        await service.DeleteAsync(shift.Id);
        await LoadAsync();
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
