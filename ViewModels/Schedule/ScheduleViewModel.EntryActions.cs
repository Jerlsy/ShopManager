using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;

namespace ShopManager.ViewModels;

public partial class ScheduleViewModel
{
    // ══════════════════════════════════════════
    // 功能一：快速新增（點擊格子）
    // ══════════════════════════════════════════
    [ObservableProperty] private bool _isQuickAdding;
    [ObservableProperty] private DateOnly _quickAddDate;
    [ObservableProperty] private Employee? _quickAddEmployee;
    [ObservableProperty] private ShiftSetting? _quickAddShift;

    [RelayCommand]
    public void OpenQuickAdd(CalendarDay day)
    {
        if (day.IsPlaceholder || day.IsClosed) return;
        if (CurrentSchedule is null) return;

        QuickAddDate     = day.Date;
        QuickAddEmployee = ActiveEmployees.FirstOrDefault();
        QuickAddShift    = EnabledShifts.FirstOrDefault();
        IsCreating       = false;
        IsBatchMode      = false;
        IsQuickAdding    = true;
    }

    [RelayCommand]
    public void CancelQuickAdd() => IsQuickAdding = false;

    [RelayCommand]
    public async Task ConfirmQuickAddAsync()
    {
        if (CurrentSchedule is null || QuickAddEmployee is null || QuickAddShift is null) return;

        var existing = CurrentSchedule.Entries.Any(e =>
            e.EmployeeId    == QuickAddEmployee.Id &&
            e.Date          == QuickAddDate &&
            e.ShiftSettingId == QuickAddShift.Id);

        if (existing)
        {
            _snackbarService.ShowError("該日期已有相同排班");
            return;
        }

        var employee = QuickAddEmployee;
        var date     = QuickAddDate;
        var shift    = QuickAddShift;

        var added = await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = CurrentSchedule.Id,
            EmployeeId        = employee.Id,
            Date              = date,
            ShiftSettingId    = shift.Id,
        });

        IsQuickAdding = false;
        ApplyEntryAddLocally(added);
        PushUndoAndNotify(
            $"新增 {employee.Name} {date:MM/dd} {shift.Alias}",
            () => _entryService.RemoveEntryAsync(added.Id),
            $"已新增 {employee.Name} {date:MM/dd} {shift.Alias}");
    }

    // ══════════════════════════════════════════
    // 功能二：右鍵選單操作
    // ══════════════════════════════════════════
    [RelayCommand]
    public async Task DeleteEntryAsync(int entryId)
    {
        var entry   = CurrentSchedule?.Entries.FirstOrDefault(e => e.Id == entryId);
        var restore = entry is null ? null : new ScheduleEntry
        {
            MonthlyScheduleId = entry.MonthlyScheduleId,
            EmployeeId        = entry.EmployeeId,
            Date              = entry.Date,
            ShiftSettingId    = entry.ShiftSettingId,
            Note              = entry.Note,
        };
        var label = $"{entry?.Employee?.Name ?? "員工"} {entry?.Date:MM/dd} {entry?.ShiftSetting?.Alias ?? ""}".Trim();

        ApplyEntryRemoveLocally(entryId);
        await _entryService.RemoveEntryAsync(entryId);

        if (restore is not null)
            PushUndoAndNotify(
                $"刪除 {label}",
                () => _entryService.AddEntryAsync(restore),
                "排班已刪除");
        else
            _snackbarService.ShowSuccess("排班已刪除");
    }

    [RelayCommand]
    public async Task CopyToNextWeekAsync(int entryId)
    {
        if (CurrentSchedule is null) return;

        var entry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        var targetDate     = entry.Date.AddDays(7);
        var sameMonth      = targetDate.Year == SelectedYear && targetDate.Month == SelectedMonth;
        var targetSchedule = sameMonth
            ? CurrentSchedule
            : await _scheduleService.GetAsync(targetDate.Year, targetDate.Month);

        if (targetSchedule is null)
        {
            _snackbarService.ShowError($"{targetDate.Year}/{targetDate.Month} 班表不存在，請先建立");
            return;
        }

        var exists = targetSchedule.Entries.Any(e =>
            e.EmployeeId     == entry.EmployeeId &&
            e.Date           == targetDate &&
            e.ShiftSettingId == entry.ShiftSettingId);

        if (exists)
        {
            _snackbarService.ShowError("目標日期已有相同排班");
            return;
        }

        var added = await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = targetSchedule.Id,
            EmployeeId        = entry.EmployeeId,
            Date              = targetDate,
            ShiftSettingId    = entry.ShiftSettingId,
        });

        if (sameMonth) ApplyEntryAddLocally(added);   // 跨月時不影響當前視圖

        PushUndoAndNotify(
            $"複製 {entry.Employee?.Name ?? "員工"} 到 {targetDate:MM/dd}",
            () => _entryService.RemoveEntryAsync(added.Id),
            $"已複製到 {targetDate:MM/dd}");
    }

    // ── 功能二補充：編輯排班 ─────────────────────
    [ObservableProperty] private bool _isEditEntryOpen;
    [ObservableProperty] private string _editEntryInfo = string.Empty;
    [ObservableProperty] private ShiftSetting? _editEntryShift;
    [ObservableProperty] private string _editEntryNote = string.Empty;
    private int _editEntryId;

    [RelayCommand]
    public void OpenEditEntry(int entryId)
    {
        if (CurrentSchedule is null) return;
        var entry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        _editEntryId   = entryId;
        EditEntryInfo  = $"{entry.Employee?.Name} — {entry.Date:MM/dd}（{GetDayOfWeekText(entry.Date.DayOfWeek)}）";
        EditEntryShift = EnabledShifts.FirstOrDefault(s => s.Id == entry.ShiftSettingId)
                         ?? EnabledShifts.FirstOrDefault();
        EditEntryNote  = entry.Note ?? string.Empty;
        IsEditEntryOpen = true;
        IsQuickAdding   = false;
        IsBatchMode     = false;
        IsCreating      = false;
    }

    [RelayCommand]
    public async Task ConfirmEditEntryAsync()
    {
        if (EditEntryShift is null) return;

        var original        = CurrentSchedule?.Entries.FirstOrDefault(e => e.Id == _editEntryId);
        var originalShiftId = original?.ShiftSettingId ?? EditEntryShift.Id;
        var originalNote    = original?.Note ?? string.Empty;
        var entryLabel      = $"{original?.Employee?.Name ?? "員工"} {original?.Date:MM/dd}";
        var id              = _editEntryId;
        var newShiftId      = EditEntryShift.Id;
        var newNote         = EditEntryNote;

        await _entryService.UpdateEntryAsync(id, newShiftId, newNote);
        IsEditEntryOpen = false;
        ApplyEntryUpdateLocally(id, newShiftId, newNote);

        PushUndoAndNotify(
            $"編輯 {entryLabel} 排班",
            () => _entryService.UpdateEntryAsync(id, originalShiftId, originalNote),
            "排班已更新");
    }

    [RelayCommand]
    public void CancelEditEntry() => IsEditEntryOpen = false;

    // ══════════════════════════════════════════
    // 拖放排班
    // ══════════════════════════════════════════
    //
    // ── 操作對應（由 View 層決定入口）─────────────────────────────────
    //  1. 交換 (Swap)   — 拖到員工頭像 → EntryChip_Drop → SwapEmployeeAsync
    //  2. 移動 (Move)   — 拖到班表空白 + 非 Ctrl → DropEmployeeAsync
    //  3. 複製 (Copy)   — 拖到班表空白 + Ctrl → DropEmployeeAsync(isCopy:true)
    //  4. 新增 (Add)    — 從員工清單拖入（無來源 EntryId）→ DropEmployeeAsync

    // 拖曳進行時由 View 設定，供 EvaluateShiftForDrop 排除來源班次自身
    public int DragSourceEntryId { get; set; } = -1;

    // ── 操作②③④：由 ShiftBlock_Drop 呼叫（移動 / 複製 / 新增）───────
    public async Task DropEmployeeAsync(Employee employee, DateOnly date, ShiftSetting shift,
        int? sourceEntryId = null, bool isCopy = false)
    {
        if (CurrentSchedule is null) return;

        var alreadyExists = CurrentSchedule.Entries.Any(e =>
            e.EmployeeId     == employee.Id &&
            e.Date           == date &&
            e.ShiftSettingId == shift.Id &&
            e.Id             != (sourceEntryId ?? -1));
        if (alreadyExists) return;

        await ExecuteAddOrMoveAsync(employee, date, shift, sourceEntryId, isCopy);
    }

    // ── 操作①：由 EntryChip_Drop 呼叫（頭像對頭像交換）────────────────
    public async Task SwapEmployeeAsync(Employee dragEmployee, int sourceEntryId, EntryItem targetEntry)
    {
        if (CurrentSchedule is null) return;

        var sourceEntry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == sourceEntryId);
        var destEntry   = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == targetEntry.EntryId);
        if (sourceEntry is null || destEntry is null) return;
        if (sourceEntry.EmployeeId == destEntry.EmployeeId) return;

        var wouldDuplicate = CurrentSchedule.Entries.Any(e =>
            e.Date           == sourceEntry.Date &&
            e.ShiftSettingId == sourceEntry.ShiftSettingId &&
            e.EmployeeId     == destEntry.EmployeeId);
        if (wouldDuplicate)
        {
            _snackbarService.ShowError($"「{destEntry.Employee?.Name ?? "員工"}」已排在該班次，無法交換");
            return;
        }

        if (targetEntry.ShiftSetting is null) return;
        await ExecuteSwapAsync(dragEmployee, targetEntry.Date, targetEntry.ShiftSetting, sourceEntry, destEntry);
    }

    // ── 操作①：交換 ────────────────────────────────────────────────
    private async Task ExecuteSwapAsync(Employee employee, DateOnly date, ShiftSetting shift,
        ScheduleEntry sourceEntry, ScheduleEntry destEntry)
    {
        var srcRestore = SnapshotEntry(sourceEntry);
        var dstRestore = SnapshotEntry(destEntry);
        var empName    = employee.Name;
        var destName   = destEntry.Employee?.Name ?? "員工";
        var srcId      = sourceEntry.Id;
        var dstId      = destEntry.Id;

        var added = await _entryService.AddEntriesAsync([
            new ScheduleEntry { MonthlyScheduleId = CurrentSchedule!.Id, EmployeeId = employee.Id,          Date = date,             ShiftSettingId = shift.Id                },
            new ScheduleEntry { MonthlyScheduleId = CurrentSchedule.Id,  EmployeeId = destEntry.EmployeeId, Date = sourceEntry.Date, ShiftSettingId = sourceEntry.ShiftSettingId },
        ]);
        await _entryService.RemoveEntriesAsync([srcId, dstId]);

        // Optimistic：先移除舊兩筆、再加入新兩筆
        ApplyEntryRemoveLocally(srcId);
        ApplyEntryRemoveLocally(dstId);
        foreach (var e in added) ApplyEntryAddLocally(e);

        if (added.Count == 2)
        {
            var addedIds = added.Select(e => e.Id).ToList();
            PushUndoAndNotify(
                $"交換 {empName} 與 {destName} 的排班",
                async () =>
                {
                    await _entryService.RemoveEntriesAsync(addedIds);
                    await _entryService.AddEntriesAsync([srcRestore, dstRestore]);
                },
                $"已交換 {empName} 與 {destName} 的排班");
        }
        else
            _snackbarService.ShowSuccess($"已交換 {empName} 與 {destName} 的排班");
    }

    // ── 操作②③④：移動 / 複製 / 新增 ──────────────────────────────
    private async Task ExecuteAddOrMoveAsync(Employee employee, DateOnly date, ShiftSetting shift,
        int? sourceEntryId, bool isCopy)
    {
        ScheduleEntry? srcSnapshot = null;
        if (sourceEntryId.HasValue && !isCopy)
        {
            var src = CurrentSchedule!.Entries.FirstOrDefault(e => e.Id == sourceEntryId.Value);
            if (src is not null) srcSnapshot = SnapshotEntry(src);
        }

        var added = await _entryService.AddEntryAsync(new ScheduleEntry
        {
            MonthlyScheduleId = CurrentSchedule!.Id,
            EmployeeId        = employee.Id,
            Date              = date,
            ShiftSettingId    = shift.Id,
        });

        // 補齊 navigation 後再做 Optimistic 更新（順序：先移除來源、再加入新項）
        if (sourceEntryId.HasValue && !isCopy)
            ApplyEntryRemoveLocally(sourceEntryId.Value);
        ApplyEntryAddLocally(added);

        if (sourceEntryId.HasValue && !isCopy)
            await _entryService.RemoveEntryAsync(sourceEntryId.Value);

        var addedId = added.Id;
        if (sourceEntryId.HasValue && !isCopy)
            PushUndoAndNotify(
                $"移動 {employee.Name} 到 {date:MM/dd} {shift.Alias}",
                async () =>
                {
                    await _entryService.RemoveEntryAsync(addedId);
                    if (srcSnapshot is not null) await _entryService.AddEntryAsync(srcSnapshot);
                },
                $"已移動 {employee.Name} 到 {date:MM/dd} {shift.Alias}");
        else
        {
            var label = isCopy ? $"複製 {employee.Name} 到 {date:MM/dd} {shift.Alias}"
                               : $"新增 {employee.Name} {date:MM/dd} {shift.Alias}";
            PushUndoAndNotify(
                label,
                () => _entryService.RemoveEntryAsync(addedId),
                "已" + label);
        }
    }

    // ── 工具：建立 ScheduleEntry 快照（用於 Undo 還原）────────────
    private static ScheduleEntry SnapshotEntry(ScheduleEntry src) => new()
    {
        MonthlyScheduleId = src.MonthlyScheduleId,
        EmployeeId        = src.EmployeeId,
        Date              = src.Date,
        ShiftSettingId    = src.ShiftSettingId,
        Note              = src.Note,
    };
}
