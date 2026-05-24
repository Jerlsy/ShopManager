using ShopManager.Models;
using ShopManager.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShopManager.Views.Schedule;

public partial class SchedulePage : UserControl
{
    public SchedulePage(ScheduleViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
        viewModel.PropertyChanged += OnOverlayPropertyChanged;
    }

    // 浮層關閉後清除鍵盤焦點，防止 WPF 焦點轉移到 ScrollViewer 內元素
    // 並透過 RequestBringIntoView 導致卷軸跳到底部
    private void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ScheduleViewModel vm) return;
        bool closing = e.PropertyName switch
        {
            nameof(ScheduleViewModel.IsEntryCardOpen)      => !vm.IsEntryCardOpen,
            nameof(ScheduleViewModel.IsDayDetailOpen)      => !vm.IsDayDetailOpen,
            nameof(ScheduleViewModel.IsConflictPanelOpen)  => !vm.IsConflictPanelOpen,
            nameof(ScheduleViewModel.IsEmployeeDetailOpen) => !vm.IsEmployeeDetailOpen,
            _ => false
        };
        if (closing)
            Dispatcher.BeginInvoke(Keyboard.ClearFocus, DispatcherPriority.Loaded);
    }

    // ── 員工拖放開始（從員工清單）：點擊 → 開啟詳情，拖曳 → 排班 ──────
    private Point _empDragStartPoint;
    private EmployeeWorkloadItem? _empDragCandidate;

    private void Employee_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _empDragStartPoint = e.GetPosition(null);
        _empDragCandidate  = (sender as FrameworkElement)?.DataContext as EmployeeWorkloadItem;
    }

    private void Employee_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _empDragCandidate is null) return;

        var pos  = e.GetPosition(null);
        var diff = _empDragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (DataContext is not ScheduleViewModel vm) return;

        var item = _empDragCandidate;
        _empDragCandidate = null;

        vm.SelectedEmployee = item.Employee;
        var data = new DataObject("Employee", item.Employee);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        vm.SelectedEmployee = null;
        DragTooltipPopup.IsOpen = false;
    }

    private void Employee_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_empDragCandidate is null) return;
        var item = _empDragCandidate;
        _empDragCandidate = null;

        if (DataContext is ScheduleViewModel vm)
        {
            vm.OpenEmployeeDetailCommand.Execute(item);
            e.Handled = true;
        }
    }

    // ── 班表間拖曳（從班次 EntryItem 開始） ──────────────
    private Point _dragStartPoint;
    private EntryItem? _dragEntryCandidate;

    private void EntryRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _dragEntryCandidate = (sender as FrameworkElement)?.DataContext as EntryItem;
    }

    private void EntryRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragEntryCandidate is null) return;

        var pos = e.GetPosition(null);
        var diff = _dragStartPoint - pos;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (DataContext is not ScheduleViewModel vm) return;

        var entry = _dragEntryCandidate;
        _dragEntryCandidate = null;

        // 使用 ActiveEmployees 中的完整員工物件（含 ScheduleRules），
        // 避免排班表 ThenInclude 載入的員工缺少規則資料導致 NotWith 誤判
        var fullEmployee = vm.ActiveEmployees.FirstOrDefault(e => e.Id == entry.Employee.Id)
                           ?? entry.Employee;
        vm.DragSourceEntryId = entry.EntryId;
        vm.SelectedEmployee = fullEmployee;
        var data = new DataObject("Employee", fullEmployee);
        data.SetData("EntryId", entry.EntryId);
        // Move | Copy：讓來源同時宣告支援兩種操作，DragOver 才能依 Ctrl 鍵切換效果
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move | DragDropEffects.Copy);
        vm.DragSourceEntryId = -1;
        vm.SelectedEmployee = null;
        DragTooltipPopup.IsOpen = false;
    }

    private void EntryRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragEntryCandidate is null) return;
        var entry = _dragEntryCandidate;
        _dragEntryCandidate = null;

        if (DataContext is not ScheduleViewModel vm) return;

        // Ctrl+Click：切換選取（不開卡片）
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            entry.IsSelected = !entry.IsSelected;
            vm.RefreshSelectionCount();
            e.Handled = true;
            return;
        }

        // 一般點擊：若有既存選取，先清除（避免誤操作）
        if (vm.HasSelection) vm.ClearSelectionCommand.Execute(null);

        vm.OpenEntryCardCommand.Execute(entry);
        e.Handled = true;
    }

    // ── 拖放到班別格子（移動 / 複製 / 新增）────────────────────────────
    // DragOver：
    //   Ctrl+EntryDrag → 複製模式，用 IsDisabledForCopy 判斷
    //   EntryDrag      → 移動模式，用 IsDisabled 判斷
    //   EmployeeDrag   → 新增模式，用 IsDisabled 判斷
    // Drop：isCopy=true 時走複製路徑；否則走移動/新增路徑
    // 注意：交換（Swap）改由 EntryChip_Drop 處理，不再在此判斷

    private void AvatarChip_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ToolTip is ToolTip tt)
        {
            tt.Placement = PlacementMode.Custom;
            tt.CustomPopupPlacementCallback = PlaceTooltipAtAvatarTop;
        }
    }

    // 箭頭尖端對齊頭像頂端中心（x = 水平置中，y = 頭像上方）
    private static CustomPopupPlacement[] PlaceTooltipAtAvatarTop(Size popupSize, Size targetSize, Point _)
    {
        double x = (targetSize.Width - popupSize.Width) / 2;
        double y = -popupSize.Height;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
    }

    private void ShiftBlock_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ToolTip is ToolTip tt)
        {
            tt.Placement = PlacementMode.Custom;
            tt.CustomPopupPlacementCallback = PlaceTooltipCenterAbove;
        }
    }

    // 箭頭尖端在目標正上方，帶 6px 間距（ShiftBlock 用）
    private static CustomPopupPlacement[] PlaceTooltipCenterAbove(Size popupSize, Size targetSize, Point _)
    {
        double x = (targetSize.Width - popupSize.Width) / 2;
        double y = -popupSize.Height - 6;
        return [new CustomPopupPlacement(new Point(x, y), PopupPrimaryAxis.Horizontal)];
    }

    private void ShiftBlock_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("Employee"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        ShiftBlock? block = null;
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is ShiftBlock b)        block = b;
            else if (fe.DataContext is ShiftBlock b2) block = b2;
        }

        bool isEntryDrag  = e.Data.GetDataPresent("EntryId");
        bool ctrlHeld     = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        bool isCopyIntent = isEntryDrag && ctrlHeld;
        bool isMoveIntent = isEntryDrag && !ctrlHeld;

        bool isBlocked = false;
        string blockedReason = string.Empty;
        if (block is not null)
        {
            if (isCopyIntent)
            {
                isBlocked     = block.IsDisabledForCopy;
                blockedReason = block.DisabledReasonForCopy;
            }
            else
            {
                isBlocked     = block.IsDisabled;
                blockedReason = block.DisabledReason;
            }
        }

        if (isBlocked)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (!string.IsNullOrEmpty(blockedReason))
            {
                DragTooltipText.Text = blockedReason;
                DragTooltipPopup.IsOpen = true;
            }
            else
            {
                DragTooltipPopup.IsOpen = false;
            }
        }
        else
        {
            e.Effects = isMoveIntent ? DragDropEffects.Move : DragDropEffects.Copy;
            e.Handled = true;
            DragTooltipPopup.IsOpen = false;
        }
    }

    private async void ShiftBlock_Drop(object sender, DragEventArgs e)
    {
        DragTooltipPopup.IsOpen = false;
        if (!e.Data.GetDataPresent("Employee")) return;
        if (DataContext is not ScheduleViewModel vm) return;

        var employee = (Employee)e.Data.GetData("Employee");
        int? sourceEntryId = e.Data.GetDataPresent("EntryId") ? (int?)e.Data.GetData("EntryId") : null;
        bool isCopy = sourceEntryId.HasValue && (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

        ShiftBlock? block = null;
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is ShiftBlock b)        block = b;
            else if (fe.DataContext is ShiftBlock b2) block = b2;
        }

        if (block is null) return;

        bool isBlocked = isCopy ? block.IsDisabledForCopy : block.IsDisabled;
        if (isBlocked) return;

        await vm.DropEmployeeAsync(employee, block.Date, block.ShiftSetting, sourceEntryId, isCopy);
        e.Handled = true;
    }

    // ── 拖放到員工頭像（交換）────────────────────────────────────────
    // 只接受 EntryDrag（非 Ctrl）；Ctrl+拖曳讓事件冒泡到 ShiftBlock_DragOver 走複製路徑
    private void EntryChip_DragOver(object sender, DragEventArgs e)
    {
        bool isEntryDrag = e.Data.GetDataPresent("EntryId");
        bool ctrlHeld    = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

        if (!isEntryDrag || ctrlHeld)
            return; // 不處理 → 冒泡到 ShiftBlock_DragOver

        if (DataContext is not ScheduleViewModel vm) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not EntryItem targetEntry) return;
        if (e.Data.GetData("Employee") is not Employee dragEmp) return;
        if (targetEntry.Employee?.Id == dragEmp.Id) return;

        int sourceEntryId = e.Data.GetData("EntryId") is int sid ? sid : -1;

        // 規則驗證：雙向都要符合
        var v = vm.ValidateSwap(dragEmp, sourceEntryId, targetEntry);
        if (v.IsBlocked)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (!string.IsNullOrEmpty(v.Reason))
            {
                DragTooltipText.Text = v.Reason;
                DragTooltipPopup.IsOpen = true;
            }
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        DragTooltipPopup.IsOpen = false;
    }

    private async void EntryChip_Drop(object sender, DragEventArgs e)
    {
        bool isEntryDrag = e.Data.GetDataPresent("EntryId");
        bool ctrlHeld    = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;

        if (!isEntryDrag || ctrlHeld)
            return; // 不處理 → 冒泡到 ShiftBlock_Drop

        if (DataContext is not ScheduleViewModel vm) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not EntryItem targetEntry) return;
        if (e.Data.GetData("Employee") is not Employee dragEmp) return;
        if (e.Data.GetData("EntryId") is not int sourceEntryId) return;
        if (targetEntry.Employee?.Id == dragEmp.Id) return;

        // 防禦性檢查：即使 DragOver 沒擋住，Drop 也要再驗證一次
        if (vm.ValidateSwap(dragEmp, sourceEntryId, targetEntry).IsBlocked) return;

        DragTooltipPopup.IsOpen = false;
        await vm.SwapEmployeeAsync(dragEmp, sourceEntryId, targetEntry);
        e.Handled = true;
    }

    // ── 日期格子點擊 → 月視圖開啟詳情，周/日視圖開啟快速新增 ───────
    private void CalendarDay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement src &&
            (src.DataContext is EntryItem || src.DataContext is ShiftBlock))
            return;

        if (sender is FrameworkElement fe
            && fe.DataContext is CalendarDay day
            && !day.IsPlaceholder
            && DataContext is ScheduleViewModel vm)
        {
            if (vm.IsMonthView)
            {
                vm.OpenDayDetailCommand.Execute(day);
                e.Handled = true;
            }
            else if (!day.IsClosed)
            {
                vm.OpenQuickAddCommand.Execute(day);
                e.Handled = true;
            }
        }
    }

    // ── 周視圖日期標題點擊 → 開啟日期詳情（跨月無班表不處理）──
    private void WeekDayHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe
            && fe.DataContext is CalendarDay day
            && !day.IsOutOfScope
            && DataContext is ScheduleViewModel vm)
        {
            vm.OpenDayDetailCommand.Execute(day);
        }
    }

    // ── 日期詳情背景點擊 → 關閉 ──
    private void DayDetailOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender && DataContext is ScheduleViewModel vm)
        {
            vm.CloseDayDetailCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── 員工大頭貼點擊（日期詳情浮層中的 chip）→ 開啟員工資訊卡 ──
    private void EntryAvatar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is EntryItem entry
            && DataContext is ScheduleViewModel vm)
        {
            vm.OpenEntryCardCommand.Execute(entry);
            e.Handled = true;
        }
    }

    // ── 員工資訊卡背景點擊 → 關閉 ──
    private void EntryCardOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender && DataContext is ScheduleViewModel vm)
        {
            vm.CloseEntryCardCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── 衝突面板背景點擊 → 關閉 ──
    private void ConflictPanelOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender && DataContext is ScheduleViewModel vm)
        {
            vm.CloseConflictPanelCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── 員工排班詳情背景點擊 → 關閉 ──
    private void EmployeeDetailOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender && DataContext is ScheduleViewModel vm)
        {
            vm.CloseEmployeeDetailCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── 推薦員工面板背景點擊 → 關閉 ──
    private void RecommendOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender && DataContext is ScheduleViewModel vm)
        {
            vm.CloseRecommendPanelCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── 轉存班表 ────────────────────────────────────────────────────────────
    private async void ExportSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ScheduleViewModel vm) return;
        var data = await vm.BuildExportDataAsync();
        if (data is null) return;
        var win = new ExportScheduleWindow(data) { Owner = Window.GetWindow(this) };
        win.ShowDialog();
    }


    // ── 滾輪事件修正 ────────────────────────────────────────────────────────
    // 問題根源：section card 內的元素（AllowDrop 班表色塊、員工卡片等）攔截了
    // MouseWheel 事件，導致事件無法冒泡到 PageScrollViewer（背景捲動正常的原因）。
    // 修法：在 section Border 的 PreviewMouseWheel（隧道事件）最先觸發時，
    //        向上走訪找到 PageScrollViewer 並直接捲動，複製游標在背景時的行為。

    private ScrollViewer? _pageScrollViewer;

    private void Section_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pageScrollViewer is null)
        {
            var current = VisualTreeHelper.GetParent((DependencyObject)sender);
            while (current is not null)
            {
                if (current is ScrollViewer sv) { _pageScrollViewer = sv; break; }
                current = VisualTreeHelper.GetParent(current);
            }
        }
        if (_pageScrollViewer is null) return;
        e.Handled = true;
        _pageScrollViewer.ScrollToVerticalOffset(_pageScrollViewer.VerticalOffset - e.Delta / 3.0);
    }
}
