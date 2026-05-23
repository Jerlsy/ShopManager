using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using System.Collections.ObjectModel;

namespace ShopManager.ViewModels;

// ══════════════════════════════════════════════
// 視圖模型輔助類別
// ══════════════════════════════════════════════

public enum CalendarViewMode { Month, Week, Day }

public record ViewModeOption(CalendarViewMode Value, string Label);

/// <summary>排班記錄顯示單位（含 EntryId 供右鍵操作使用）</summary>
public class EntryItem
{
    public int EntryId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly Date { get; set; }
    public ShiftSetting? ShiftSetting { get; set; }
}

public partial class CalendarDay : ObservableObject
{
    public DateOnly Date { get; set; }
    public int Day { get; set; }
    public string DayOfWeekText { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
    public bool IsToday { get; set; }
    public bool IsWeekend { get; set; }
    public bool IsSelected { get; set; }
    public bool IsPlaceholder { get; set; }
    public bool HasStaffingGap { get; set; }
    public bool IsOutOfScope { get; set; }   // 周視圖跨月且無班表
    public string? HolidayName { get; set; }
    public ObservableCollection<ShiftBlock> ShiftBlocks { get; } = new();

    // 空班別狀態（由 Optimistic UI helpers 更新）
    [ObservableProperty] private int _emptyShiftCount;
    public bool HasEmptyShifts => EmptyShiftCount > 0;
    partial void OnEmptyShiftCountChanged(int value) => OnPropertyChanged(nameof(HasEmptyShifts));

    public void RecomputeEmptyShiftCount()
        => EmptyShiftCount = ShiftBlocks.Count(b => b.IsEmpty);
}

public class CalendarWeekRow
{
    public int WeekNumber { get; set; }
    public List<CalendarDay> Days { get; } = new();
}

public partial class ShiftBlock : ObservableObject
{
    public ShiftSetting ShiftSetting { get; set; } = null!;
    public DateOnly Date { get; set; }

    private ObservableCollection<EntryItem> _entryItems = new();
    public ObservableCollection<EntryItem> EntryItems
    {
        get => _entryItems;
        set
        {
            if (ReferenceEquals(_entryItems, value)) return;
            _entryItems.CollectionChanged -= OnEntryItemsChanged;
            _entryItems = value;
            _entryItems.CollectionChanged += OnEntryItemsChanged;
            IsEmpty = _entryItems.Count == 0;
        }
    }

    private void OnEntryItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => IsEmpty = _entryItems.Count == 0;

    // 移動模式：全部規則 #1-#6（能否進入此班表位置）
    [ObservableProperty] private bool _isDisabled;
    [ObservableProperty] private string _disabledReason = string.Empty;
    // 複製模式：僅群組 C（#5 時間重疊 + #6 每日工時）
    [ObservableProperty] private bool _isDisabledForCopy;
    [ObservableProperty] private string _disabledReasonForCopy = string.Empty;
    // 空班別狀態：EntryItems 為空時為 true（由 EntryItems CollectionChanged 驅動更新）
    [ObservableProperty] private bool _isEmpty = true;
    // 時間軸定位（周/日視圖）
    public double BlockTop { get; set; }
    public double BlockHeight { get; set; }
    public System.Windows.Thickness BlockMargin => new(2, BlockTop, 2, 0);

    public ShiftBlock()
    {
        _entryItems.CollectionChanged += OnEntryItemsChanged;
    }
}

public class CalendarTimeSlot
{
    public int Hour { get; set; }
    public string Label { get; set; } = string.Empty;
    public ObservableCollection<DayTimeSlot> Days { get; } = new();
}

public class DayTimeSlot
{
    public DateOnly Date { get; set; }
    public int Hour { get; set; }
    public bool IsClosed { get; set; }
    public ObservableCollection<ShiftBlock> ShiftBlocks { get; } = new();
}

public partial class ShiftDayCell : ObservableObject
{
    public DayOfWeek Day { get; init; }
    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    private bool _isShopClosed;

    public bool IsEnabled => !IsShopClosed;

    [ObservableProperty] private bool _isChecked;
}

public partial class ShiftDayAssignmentItem : ObservableObject
{
    public IReadOnlyList<ShiftSetting> AvailableShifts { get; init; } = [];
    [ObservableProperty] private ShiftSetting? _selectedShift;
    public ObservableCollection<ShiftDayCell> DayCells { get; } = new();
}

public class NationalHolidayItem
{
    public int Day { get; init; }
    public string Label { get; init; } = string.Empty;
}

public partial class ShiftOverrideCell : ObservableObject
{
    public ShiftSetting Shift { get; init; } = null!;
    [ObservableProperty] private bool _isChecked;
}

public partial class EmployeeWorkloadItem : ObservableObject
{
    public Employee Employee { get; init; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnder))]
    [NotifyPropertyChangedFor(nameof(IsMet))]
    [NotifyPropertyChangedFor(nameof(IsOver))]
    private int _shiftCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnder))]
    [NotifyPropertyChangedFor(nameof(IsMet))]
    [NotifyPropertyChangedFor(nameof(IsOver))]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    private int _targetCount;

    public bool HasTarget => TargetCount > 0;
    public bool IsUnder   => TargetCount > 0 && ShiftCount < TargetCount;
    public bool IsMet     => TargetCount > 0 && ShiftCount == TargetCount;
    public bool IsOver    => TargetCount > 0 && ShiftCount > TargetCount;
}

public partial class WorkDayConditionCell : ObservableObject
{
    public DayOfWeek Day { get; init; }
    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    private bool _isShopClosed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled))]
    private bool _isAlreadyUsed;

    public bool IsEnabled => !IsShopClosed && !IsAlreadyUsed;

    [ObservableProperty] private bool _isChecked;
}

public partial class WorkDayConditionItem : ObservableObject
{
    [ObservableProperty] private int _maxPerShift = 1;
    [ObservableProperty] private int _minPerDay = 1;
    public ObservableCollection<WorkDayConditionCell> DayCells { get; } = new();

    partial void OnMaxPerShiftChanged(int value) { if (value < 1) MaxPerShift = 1; }
    partial void OnMinPerDayChanged(int value)   { if (value < 1) MinPerDay = 1; }
}

public class EmployeeDetailEntry
{
    public string DateText      { get; init; } = string.Empty;
    public string DayOfWeekText { get; init; } = string.Empty;
    public string ShiftAlias    { get; init; } = string.Empty;
    public string ColorHex      { get; init; } = "#4A90D9";
    public string TimeRange     { get; init; } = string.Empty;
    public IReadOnlyList<Employee> Colleagues { get; init; } = [];
}

public class EmployeeDetailShiftStat
{
    public string ShiftAlias { get; init; } = string.Empty;
    public string ColorHex   { get; init; } = "#4A90D9";
    public int    Count      { get; init; }
}

public class RecommendEmployeeItem
{
    public Employee Employee { get; init; } = null!;
    public List<EmployeeDetailShiftStat> ShiftStatsRow1 { get; init; } = [];
    public List<EmployeeDetailShiftStat> ShiftStatsRow2 { get; init; } = [];
}

public class EmployeeRuleDisplayItem
{
    public string TypeLabel { get; init; } = string.Empty;
    public string Detail    { get; init; } = string.Empty;
}

public enum EmployeeConstraintType { DayOff = 0, ShiftPriority = 1, WorkDay = 2, ExcludeAutoAssign = 3 }

public partial class DaySelectCell : ObservableObject
{
    public int Day { get; init; }
    public ObservableCollection<int> DaysCollection { get; init; } = null!;

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        if (value && !DaysCollection.Contains(Day))
        {
            var idx = DaysCollection.TakeWhile(d => d < Day).Count();
            DaysCollection.Insert(idx, Day);
        }
        else if (!value)
        {
            DaysCollection.Remove(Day);
        }
    }
}

public partial class EmployeeConstraintItem : ObservableObject
{
    [ObservableProperty] private Employee? _selectedEmployee;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDayOff))]
    [NotifyPropertyChangedFor(nameof(IsShiftPriority))]
    [NotifyPropertyChangedFor(nameof(IsWorkDay))]
    [NotifyPropertyChangedFor(nameof(IsExcludeAutoAssign))]
    private EmployeeConstraintType _constraintType = EmployeeConstraintType.DayOff;

    public bool IsDayOff          => ConstraintType == EmployeeConstraintType.DayOff;
    public bool IsShiftPriority   => ConstraintType == EmployeeConstraintType.ShiftPriority;
    public bool IsWorkDay         => ConstraintType == EmployeeConstraintType.WorkDay;
    public bool IsExcludeAutoAssign => ConstraintType == EmployeeConstraintType.ExcludeAutoAssign;

    [ObservableProperty] private ShiftSetting? _shiftToAdd;

    public ObservableCollection<int>          DayOffDays    { get; } = new();
    public ObservableCollection<int>          WorkDayDays   { get; } = new();
    public ObservableCollection<ShiftSetting> PriorityShifts { get; } = new();

    public ObservableCollection<DaySelectCell> DayOffCells  { get; } = new();
    public ObservableCollection<DaySelectCell> WorkDayCells { get; } = new();
    public ObservableCollection<EmployeeConstraintType> AvailableConstraintTypes { get; } = new();

    public void InitializeDayCells(int year, int month, IReadOnlyCollection<int> closedDays)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        DayOffCells.Clear();
        WorkDayCells.Clear();
        for (int d = 1; d <= daysInMonth; d++)
        {
            if (closedDays.Contains(d)) continue;
            DayOffCells.Add(new DaySelectCell  { Day = d, DaysCollection = DayOffDays,  IsChecked = DayOffDays.Contains(d)  });
            WorkDayCells.Add(new DaySelectCell { Day = d, DaysCollection = WorkDayDays, IsChecked = WorkDayDays.Contains(d) });
        }
    }

    // 切換類型時自動清除與新類型衝突的舊資料
    partial void OnConstraintTypeChanged(EmployeeConstraintType value)
    {
        switch (value)
        {
            case EmployeeConstraintType.DayOff:
                foreach (var c in WorkDayCells) c.IsChecked = false;
                PriorityShifts.Clear();
                break;
            case EmployeeConstraintType.WorkDay:
                foreach (var c in DayOffCells) c.IsChecked = false;
                PriorityShifts.Clear();
                break;
            case EmployeeConstraintType.ShiftPriority:
                foreach (var c in DayOffCells)  c.IsChecked = false;
                foreach (var c in WorkDayCells) c.IsChecked = false;
                break;
            case EmployeeConstraintType.ExcludeAutoAssign:
                foreach (var c in DayOffCells)  c.IsChecked = false;
                foreach (var c in WorkDayCells) c.IsChecked = false;
                PriorityShifts.Clear();
                break;
        }
    }

    [RelayCommand]
    private void AddPriorityShift()
    {
        if (ShiftToAdd is null || PriorityShifts.Any(s => s.Id == ShiftToAdd.Id)) return;
        PriorityShifts.Add(ShiftToAdd);
        ShiftToAdd = null;
    }

    [RelayCommand]
    private void RemovePriorityShift(ShiftSetting shift) => PriorityShifts.Remove(shift);
}
