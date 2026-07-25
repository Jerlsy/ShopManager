using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;

namespace ShopManager.ViewModels;

/// <summary>觸控點選流程目前待完成的動作（取代滑鼠拖曳，滑鼠拖曳功能維持不變、兩者並存）</summary>
public enum TouchPendingAction { None, Swap, Move, Add }

public partial class ScheduleViewModel
{
    // ══════════════════════════════════════════
    // 觸控點選操作
    // 滑鼠 DragDrop.DoDragDrop 在觸控裝置上不可靠（OLE 拖放迴圈認實體滑鼠鍵狀態，
    // 觸控轉譯進去常常拖不動／追蹤不到）。改提供「點選→點選」兩段式操作作為替代路徑：
    //   已排班頭像：點一下 → 跳出「交換／移動／刪除」選單
    //     交換：再點另一位員工頭像 → 互換兩人排班
    //     移動：再點目的班別 → 該員工搬移過去
    //   員工清單：點一下 → 直接進入「待新增」，再點目的班別 → 新增排入
    // 沿用既有 SelectedEmployee / DragSourceEntryId 機制，讓待處理期間月曆會跟拖曳時一樣
    // 即時標示可排 / 不可排（IsDisabled），驗證與寫入邏輯全部重用既有 Swap / DropEmployee 方法。
    // ══════════════════════════════════════════
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTouchPendingAction))]
    private TouchPendingAction _touchPendingAction = TouchPendingAction.None;
    [ObservableProperty] private string _touchPendingLabel = string.Empty;
    public bool HasTouchPendingAction => TouchPendingAction != TouchPendingAction.None;

    private EntryItem? _touchSourceEntry;
    private Employee? _touchSourceEmployee;

    [RelayCommand]
    private void StartTouchSwap(EntryItem entry)
    {
        _touchSourceEntry    = entry;
        _touchSourceEmployee = null;
        TouchPendingAction   = TouchPendingAction.Swap;
        TouchPendingLabel    = $"已選取「{entry.Employee?.Name}」，點擊要交換的員工";
    }

    [RelayCommand]
    private void StartTouchMove(EntryItem entry)
    {
        _touchSourceEntry    = entry;
        _touchSourceEmployee = null;
        TouchPendingAction   = TouchPendingAction.Move;
        TouchPendingLabel    = $"已選取「{entry.Employee?.Name}」，點擊要移動到的班別";
        // 沿用拖曳時的即時標示機制：設定 SelectedEmployee / DragSourceEntryId 讓月曆立刻標示可排/不可排
        SelectedEmployee  = ActiveEmployees.FirstOrDefault(e => e.Id == entry.Employee!.Id) ?? entry.Employee;
        DragSourceEntryId = entry.EntryId;
    }

    /// <summary>由員工清單點選觸發（View 呼叫，非 RelayCommand：CommandParameter 為 Employee）</summary>
    public void StartTouchAdd(Employee employee)
    {
        _touchSourceEntry    = null;
        _touchSourceEmployee = employee;
        TouchPendingAction   = TouchPendingAction.Add;
        TouchPendingLabel    = $"已選取「{employee.Name}」，點擊要加入的班別";
        SelectedEmployee     = employee;
        DragSourceEntryId    = -1;
    }

    [RelayCommand]
    private void CancelTouchPendingAction()
    {
        TouchPendingAction   = TouchPendingAction.None;
        TouchPendingLabel    = string.Empty;
        _touchSourceEntry    = null;
        _touchSourceEmployee = null;
        SelectedEmployee     = null;
        DragSourceEntryId    = -1;
    }

    /// <summary>Swap 模式：點選另一位員工頭像完成交換</summary>
    public async Task CompleteTouchSwapAsync(EntryItem targetEntry)
    {
        if (TouchPendingAction != TouchPendingAction.Swap || _touchSourceEntry is null) return;
        var source = _touchSourceEntry;

        if (source.EntryId == targetEntry.EntryId)
        {
            CancelTouchPendingAction(); // 點回自己＝取消
            return;
        }

        var v = ValidateSwap(source.Employee, source.EntryId, targetEntry);
        CancelTouchPendingAction();
        if (v.IsBlocked)
        {
            if (!string.IsNullOrEmpty(v.Reason)) _snackbarService.ShowWarning(v.Reason);
            return;
        }
        await SwapEmployeeAsync(source.Employee, source.EntryId, targetEntry);
    }

    /// <summary>Move／Add 模式：點選目的班別完成移動或新增</summary>
    public async Task CompleteTouchMoveOrAddAsync(DateOnly date, ShiftSetting shift)
    {
        if (TouchPendingAction is not (TouchPendingAction.Move or TouchPendingAction.Add)) return;
        bool isMove       = TouchPendingAction == TouchPendingAction.Move;
        var employee      = isMove ? _touchSourceEntry?.Employee : _touchSourceEmployee;
        var sourceEntryId = isMove ? _touchSourceEntry?.EntryId : (int?)null;

        // 驗證要在 CancelTouchPendingAction 清除 SelectedEmployee/DragSourceEntryId 之前做
        var v = employee is not null ? EvaluateShiftForDrop(date, shift) : ShiftValidationResult.Allow;
        CancelTouchPendingAction();
        if (employee is null) return;

        if (v.IsBlocked)
        {
            if (!string.IsNullOrEmpty(v.Reason)) _snackbarService.ShowWarning(v.Reason);
            return;
        }
        await DropEmployeeAsync(employee, date, shift, sourceEntryId, isCopy: false);
    }
}
