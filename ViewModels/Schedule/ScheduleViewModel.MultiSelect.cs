using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;

namespace ShopManager.ViewModels;

// ══════════════════════════════════════════════════════════════════════
// 多選 + 批次刪除：EntryItem.IsSelected 由 View 層（Ctrl+Click）切換，
// 此處統計 + 提供批次操作命令
// ══════════════════════════════════════════════════════════════════════
public partial class ScheduleViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private int _selectionCount;

    public bool HasSelection => SelectionCount > 0;

    /// <summary>View 層在切換 EntryItem.IsSelected 後呼叫此方法刷新計數</summary>
    public void RefreshSelectionCount()
    {
        int count = 0;
        foreach (var day in CalendarDays)
        {
            if (day.IsPlaceholder) continue;
            foreach (var block in day.ShiftBlocks)
                foreach (var item in block.EntryItems)
                    if (item.IsSelected) count++;
        }
        // DayDetail 浮層的 group 與 CalendarDays 中的 ShiftBlock 為不同實例，需獨立統計
        if (IsDayDetailOpen)
        {
            foreach (var block in DayDetailGroups)
                foreach (var item in block.EntryItems)
                    if (item.IsSelected) count++;
        }
        SelectionCount = count;
    }

    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var day in CalendarDays)
        {
            if (day.IsPlaceholder) continue;
            foreach (var block in day.ShiftBlocks)
                foreach (var item in block.EntryItems)
                    item.IsSelected = false;
        }
        if (IsDayDetailOpen)
        {
            foreach (var block in DayDetailGroups)
                foreach (var item in block.EntryItems)
                    item.IsSelected = false;
        }
        SelectionCount = 0;
    }

    [RelayCommand]
    public async Task DeleteSelectedAsync()
    {
        if (CurrentSchedule is null || SelectionCount == 0) return;

        var ids = new List<int>();
        var snapshots = new List<ScheduleEntry>();
        foreach (var day in CalendarDays)
        {
            if (day.IsPlaceholder) continue;
            foreach (var block in day.ShiftBlocks)
                foreach (var item in block.EntryItems)
                    if (item.IsSelected && !ids.Contains(item.EntryId))
                    {
                        ids.Add(item.EntryId);
                        var entry = CurrentSchedule.Entries.FirstOrDefault(e => e.Id == item.EntryId);
                        if (entry is not null)
                            snapshots.Add(new ScheduleEntry
                            {
                                MonthlyScheduleId = entry.MonthlyScheduleId,
                                EmployeeId        = entry.EmployeeId,
                                Date              = entry.Date,
                                ShiftSettingId    = entry.ShiftSettingId,
                                Note              = entry.Note,
                            });
                    }
        }
        if (ids.Count == 0) return;

        // Optimistic：先就地移除每筆
        foreach (var id in ids) ApplyEntryRemoveLocally(id);
        SelectionCount = 0;

        // 後端批次刪除
        await _entryService.RemoveEntriesAsync(ids);

        var count = ids.Count;
        PushUndoAndNotify(
            $"批次刪除 {count} 筆排班",
            () => _entryService.AddEntriesAsync(snapshots),
            $"已刪除 {count} 筆排班");
    }
}
