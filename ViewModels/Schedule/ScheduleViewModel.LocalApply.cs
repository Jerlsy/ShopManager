using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ShopManager.ViewModels;

// ══════════════════════════════════════════════════════════════════════
// Optimistic UI：拖放/新增/編輯/刪除 排班後不重抓全月資料，僅就地更新受影響元素
// ══════════════════════════════════════════════════════════════════════
public partial class ScheduleViewModel
{
    private DispatcherTimer? _conflictRefreshTimer;

    // ──────────────────────────────────────────────────────────────────
    // 入口：新增 / 移除 / 編輯（含 ScheduleEntry 物件參數的版本）
    // 呼叫前已對 DB 寫入；此處只負責同步記憶體資料與 UI 元素
    // ──────────────────────────────────────────────────────────────────
    private void ApplyEntryAddLocally(ScheduleEntry entry)
    {
        if (CurrentSchedule is null) return;
        if (entry.MonthlyScheduleId != CurrentSchedule.Id) return;   // 跨月不影響當前視圖

        // 1. 補齊 navigation properties（DB 返回的物件可能沒載入）
        entry.Employee     ??= ActiveEmployees.FirstOrDefault(e => e.Id == entry.EmployeeId);
        entry.ShiftSetting ??= EnabledShifts.FirstOrDefault(s => s.Id == entry.ShiftSettingId);

        // 2. 加入 CurrentSchedule.Entries
        if (!CurrentSchedule.Entries.Any(e => e.Id == entry.Id))
            CurrentSchedule.Entries.Add(entry);

        // 3. 找到對應 ShiftBlock，加入 EntryItem
        EntryItem? newItem = null;
        var block = FindShiftBlock(entry.Date, entry.ShiftSettingId);
        if (block is not null && entry.Employee is not null
            && !block.EntryItems.Any(ei => ei.EntryId == entry.Id))
        {
            newItem = new EntryItem
            {
                EntryId      = entry.Id,
                Employee     = entry.Employee,
                Date         = entry.Date,
                ShiftSetting = block.ShiftSetting,
            };
            block.EntryItems.Add(newItem);
        }

        // 4. DayDetail 浮層同步加入
        ApplyToDayDetailIfOpen(entry.Date, entry.ShiftSettingId, addItem: newItem, removeEntryId: null);

        // 5. 更新空班別計數（ShiftBlock.IsEmpty 由 CollectionChanged 自動更新）
        UpdateCalendarDayEmptyCount(entry.Date);

        // 6. 失效快取 + 重評估同日同班別其他 ShiftBlock 的禁用狀態
        InvalidateEvalCache(entry.Date, entry.ShiftSettingId);
        ReevaluateBlocksForDate(entry.Date);

        // 7. 更新員工本月排班數
        AdjustEmployeeWorkload(entry.EmployeeId, +1);

        // 8. 排程衝突計數刷新
        ScheduleConflictRefresh();
    }

    private void ApplyEntryRemoveLocally(int entryId)
    {
        if (CurrentSchedule is null) return;
        var entry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        var date    = entry.Date;
        var shiftId = entry.ShiftSettingId;
        var empId   = entry.EmployeeId;

        CurrentSchedule.Entries.Remove(entry);

        var block = FindShiftBlock(date, shiftId);
        if (block is not null)
        {
            var item = block.EntryItems.FirstOrDefault(ei => ei.EntryId == entryId);
            if (item is not null) block.EntryItems.Remove(item);
        }

        // DayDetail 浮層同步移除
        ApplyToDayDetailIfOpen(date, shiftId, addItem: null, removeEntryId: entryId);

        UpdateCalendarDayEmptyCount(date);
        InvalidateEvalCache(date, shiftId);
        ReevaluateBlocksForDate(date);
        AdjustEmployeeWorkload(empId, -1);
        ScheduleConflictRefresh();
    }

    private void ApplyEntryUpdateLocally(int entryId, int newShiftSettingId, string note)
    {
        if (CurrentSchedule is null) return;
        var entry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        var oldShiftId = entry.ShiftSettingId;
        entry.ShiftSettingId = newShiftSettingId;
        entry.ShiftSetting   = EnabledShifts.FirstOrDefault(s => s.Id == newShiftSettingId) ?? entry.ShiftSetting;
        entry.Note = note;

        if (oldShiftId == newShiftSettingId) return;   // 只改備註

        // 從舊 ShiftBlock 移除、加到新 ShiftBlock
        var oldBlock = FindShiftBlock(entry.Date, oldShiftId);
        var newBlock = FindShiftBlock(entry.Date, newShiftSettingId);

        if (oldBlock is not null)
        {
            var item = oldBlock.EntryItems.FirstOrDefault(ei => ei.EntryId == entryId);
            if (item is not null) oldBlock.EntryItems.Remove(item);
        }

        EntryItem? newItem = null;
        if (newBlock is not null && entry.Employee is not null
            && !newBlock.EntryItems.Any(ei => ei.EntryId == entryId))
        {
            newItem = new EntryItem
            {
                EntryId      = entryId,
                Employee     = entry.Employee,
                Date         = entry.Date,
                ShiftSetting = newBlock.ShiftSetting,
            };
            newBlock.EntryItems.Add(newItem);
        }

        // DayDetail 浮層同步：從舊班別 group 移除、加到新班別 group
        ApplyToDayDetailIfOpen(entry.Date, oldShiftId,        addItem: null,    removeEntryId: entryId);
        ApplyToDayDetailIfOpen(entry.Date, newShiftSettingId, addItem: newItem, removeEntryId: null);

        UpdateCalendarDayEmptyCount(entry.Date);
        InvalidateEvalCache(entry.Date, oldShiftId);
        InvalidateEvalCache(entry.Date, newShiftSettingId);
        ReevaluateBlocksForDate(entry.Date);
        ScheduleConflictRefresh();
    }

    // ──────────────────────────────────────────────────────────────────
    // 局部更新工具
    // ──────────────────────────────────────────────────────────────────
    private ShiftBlock? FindShiftBlock(DateOnly date, int shiftSettingId)
    {
        var day = CalendarDays.FirstOrDefault(d => !d.IsPlaceholder && d.Date == date);
        return day?.ShiftBlocks.FirstOrDefault(b => b.ShiftSetting.Id == shiftSettingId);
    }

    private void UpdateCalendarDayEmptyCount(DateOnly date)
    {
        var day = CalendarDays.FirstOrDefault(d => !d.IsPlaceholder && d.Date == date);
        if (day is null) return;
        day.RecomputeEmptyShiftCount();

        int total = 0;
        foreach (var d in CalendarDays)
        {
            if (d.IsPlaceholder || d.IsClosed || d.IsOutOfScope) continue;
            total += d.EmptyShiftCount;
        }
        EmptyShiftTotalCount = total;

        // 若日期詳情浮層開啟在同一天，同步重建一次 group 內的空狀態
        if (IsDayDetailOpen && DayDetailDay?.Date == date)
        {
            // DayDetailGroups 也是 ShiftBlock；同步空狀態
            foreach (var b in DayDetailGroups)
                b.IsEmpty = b.EntryItems.Count == 0;
        }
    }

    // SelectedEmployee 拖曳中，需要重評估所有與該員工相關的 ShiftBlock 禁用狀態
    // 此處僅重評估同日的（其他日期未變，且快取已 invalidate 必要條目）
    private void ReevaluateBlocksForDate(DateOnly date)
    {
        if (SelectedEmployee is null || CurrentSchedule is null) return;
        var day = CalendarDays.FirstOrDefault(d => !d.IsPlaceholder && d.Date == date);
        if (day is null) return;

        foreach (var block in day.ShiftBlocks)
        {
            var v     = EvaluateShiftForDrop(date, block.ShiftSetting);
            var vCopy = EvaluateShiftForDropCopy(date, block.ShiftSetting);
            block.IsDisabled            = v.IsBlocked;
            block.DisabledReason        = v.Reason;
            block.IsDisabledForCopy     = vCopy.IsBlocked;
            block.DisabledReasonForCopy = vCopy.Reason;
        }
    }

    private void AdjustEmployeeWorkload(int employeeId, int delta)
    {
        var item = EmployeeWorkloads.FirstOrDefault(w => w.Employee.Id == employeeId);
        if (item is not null) item.ShiftCount += delta;
    }

    // 同時也同步 DayDetail（若開啟且日期相同）
    private void ApplyToDayDetailIfOpen(DateOnly date, int shiftSettingId, EntryItem? addItem, int? removeEntryId)
    {
        if (!IsDayDetailOpen || DayDetailDay?.Date != date) return;
        var block = DayDetailGroups.FirstOrDefault(b => b.ShiftSetting.Id == shiftSettingId);
        if (block is null) return;

        if (addItem is not null && !block.EntryItems.Any(ei => ei.EntryId == addItem.EntryId))
            block.EntryItems.Add(addItem);

        if (removeEntryId.HasValue)
        {
            var item = block.EntryItems.FirstOrDefault(ei => ei.EntryId == removeEntryId.Value);
            if (item is not null) block.EntryItems.Remove(item);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // 衝突計數延遲刷新：500ms 內無新動作才向 DB 重算
    // ──────────────────────────────────────────────────────────────────
    private void ScheduleConflictRefresh()
    {
        if (CurrentSchedule is null) return;

        _conflictRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _conflictRefreshTimer.Tick -= ConflictRefreshTick;
        _conflictRefreshTimer.Tick += ConflictRefreshTick;
        _conflictRefreshTimer.Stop();
        _conflictRefreshTimer.Start();
    }

    private async void ConflictRefreshTick(object? sender, EventArgs e)
    {
        _conflictRefreshTimer?.Stop();
        if (CurrentSchedule is null) return;
        try
        {
            ConflictCount = await _conflictService.RecheckAsync(CurrentSchedule.Id);
            if (IsConflictPanelOpen)
            {
                var items = await _conflictService.GetAsync(CurrentSchedule.Id);
                ConflictItems.Clear();
                foreach (var c in items) ConflictItems.Add(c);
            }
        }
        catch { /* 後台刷新失敗不打斷使用者 */ }
    }
}
