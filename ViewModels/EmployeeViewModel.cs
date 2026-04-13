using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace ShopManager.ViewModels;

// ── 排班規則編輯用 UI 模型 ─────────────────────────────────

/// <summary>可勾選的周幾選項（固定休假用）</summary>
public partial class DayOfWeekItem : ObservableObject
{
    public DayOfWeek Day { get; init; }
    public string Label { get; init; } = string.Empty;
    [ObservableProperty] private bool _isChecked;
}

/// <summary>可勾選的班別選項（排除班別用）</summary>
public partial class ShiftCheckItem : ObservableObject
{
    public int ShiftId { get; init; }
    public string Alias { get; init; } = string.Empty;
    [ObservableProperty] private bool _isChecked;
}

/// <summary>可勾選的同事選項（不與共事用）</summary>
public partial class ColleagueCheckItem : ObservableObject
{
    public int EmployeeId { get; init; }
    public string Name { get; init; } = string.Empty;
    [ObservableProperty] private bool _isChecked;
}

// ── 主 ViewModel ──────────────────────────────────────────

public partial class EmployeeViewModel : ObservableObject
{
    private readonly EmployeeService _employeeService;
    private readonly ShiftSettingService _shiftService;
    private readonly SalarySettingService _salaryService;
    private readonly ISnackbarService _snackbarService;
    private readonly IContentDialogService _contentDialogService;

    public EmployeeViewModel(EmployeeService employeeService,
        ShiftSettingService shiftService, SalarySettingService salaryService,
        ISnackbarService snackbarService, IContentDialogService contentDialogService)
    {
        _employeeService = employeeService;
        _shiftService = shiftService;
        _salaryService = salaryService;
        _snackbarService = snackbarService;
        _contentDialogService = contentDialogService;

        // 初始化周幾清單（固定順序：一到日）
        FixedOffDayItems = new List<DayOfWeekItem>
        {
            new() { Day = DayOfWeek.Monday,    Label = "週一" },
            new() { Day = DayOfWeek.Tuesday,   Label = "週二" },
            new() { Day = DayOfWeek.Wednesday, Label = "週三" },
            new() { Day = DayOfWeek.Thursday,  Label = "週四" },
            new() { Day = DayOfWeek.Friday,    Label = "週五" },
            new() { Day = DayOfWeek.Saturday,  Label = "週六" },
            new() { Day = DayOfWeek.Sunday,    Label = "週日" },
        };
    }

    // ── 員工清單 ──────────────────────────────────────────
    [ObservableProperty] private List<Employee> _employees = new();
    [ObservableProperty] private Employee? _selectedEmployee;
    [ObservableProperty] private bool _isEditing;

    // ── 下拉選單來源 ───────────────────────────────────────
    [ObservableProperty] private List<ShiftSetting> _availableShifts = new();
    [ObservableProperty] private List<SalarySetting> _availableSalaries = new();

    // ── 排班規則 UI 用清單 ─────────────────────────────────
    /// <summary>固定休假：周一到周日的勾選清單</summary>
    public List<DayOfWeekItem> FixedOffDayItems { get; }

    /// <summary>排除班別：啟用班別的勾選清單</summary>
    [ObservableProperty] private List<ShiftCheckItem> _excludeShiftItems = new();

    /// <summary>不與共事：在職員工的勾選清單（排除自己）</summary>
    [ObservableProperty] private List<ColleagueCheckItem> _notWithItems = new();

    // ── 基本編輯欄位 ───────────────────────────────────────
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editIdNumber = string.Empty;
    [ObservableProperty] private string _editAddress = string.Empty;
    [ObservableProperty] private string _editPhone = string.Empty;
    [ObservableProperty] private string? _editMessengerType;
    [ObservableProperty] private string? _editMessengerValue;
    [ObservableProperty] private List<CustomContact> _editCustomContacts = new();
    [ObservableProperty] private ShiftSetting? _editDefaultShift;
    [ObservableProperty] private SalarySetting? _editDefaultSalary;
    [ObservableProperty] private List<DefaultBonus> _editDefaultBonuses = new();
    [ObservableProperty] private DateOnly _editHireDate = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private DateOnly? _editResignDate;

    // ── 排班衝突 ───────────────────────────────────────────
    [ObservableProperty] private bool _hasConflicts;
    [ObservableProperty] private List<string> _conflictMonths = new();

    // ── 靜態資源 ───────────────────────────────────────────
    public static List<string> MessengerTypes { get; } = new()
    {
        "Line", "Messenger", "WeChat", "WhatsApp", "Telegram", "Signal"
    };

    // ══════════════════════════════════════════════════════
    // 載入資料
    // ══════════════════════════════════════════════════════
    public async Task LoadAsync()
    {
        Employees = await _employeeService.GetAllAsync();
        AvailableShifts = (await _shiftService.GetAllAsync()).Where(s => s.IsEnabled).ToList();
        AvailableSalaries = await _salaryService.GetAllAsync();
    }

    /// <summary>載入並初始化排班規則勾選清單</summary>
    private async Task LoadScheduleRuleSourcesAsync(int? currentEmployeeId = null)
    {
        // 排除班別清單（啟用的班別）
        var shifts = await _shiftService.GetAllAsync();
        ExcludeShiftItems = shifts
            .Where(s => s.IsEnabled)
            .Select(s => new ShiftCheckItem { ShiftId = s.Id, Alias = s.Alias })
            .ToList();

        // 不與共事清單（在職員工，排除自己）
        var allActive = Employees.Where(e =>
            !e.IsResigned &&
            e.Id != (currentEmployeeId ?? -1))
            .ToList();

        NotWithItems = allActive
            .Select(e => new ColleagueCheckItem { EmployeeId = e.Id, Name = e.Name })
            .ToList();
    }

    // ══════════════════════════════════════════════════════
    // 新增員工
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public async Task StartNewAsync()
    {
        await LoadAsync();
        await LoadScheduleRuleSourcesAsync(null);

        SelectedEmployee = null;
        ClearEditFields();
        IsEditing = true;
    }

    // ══════════════════════════════════════════════════════
    // 編輯員工
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public async Task StartEditAsync(Employee emp)
    {
        await LoadAsync();
        await LoadScheduleRuleSourcesAsync(emp.Id);

        SelectedEmployee = emp;
        EditName = emp.Name;
        EditIdNumber = emp.IdNumber;
        EditAddress = emp.Address;
        EditPhone = emp.Phone;
        EditMessengerType = emp.MessengerType;
        EditMessengerValue = emp.MessengerValue;
        EditCustomContacts = new List<CustomContact>(emp.CustomContacts);
        EditDefaultShift = AvailableShifts.FirstOrDefault(s => s.Id == emp.DefaultShiftId);
        EditDefaultSalary = AvailableSalaries.FirstOrDefault(s => s.Id == emp.DefaultSalaryId);
        EditDefaultBonuses = new List<DefaultBonus>(emp.DefaultBonuses);
        EditHireDate = emp.HireDate;
        EditResignDate = emp.ResignDate;

        // ── 還原排班規則勾選狀態 ──
        // 找到現有的各類規則
        var fixedOffRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.FixedOff);
        var excludeShiftRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.ExcludeShift);
        var notWithRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.NotWith);

        // 固定休假 — 勾選對應周幾
        var fixedOffDays = fixedOffRule?.FixedOffDays ?? new List<int>();
        foreach (var item in FixedOffDayItems)
            item.IsChecked = fixedOffDays.Contains((int)item.Day);

        // 排除班別 — 勾選對應班別
        var excludedShiftIds = excludeShiftRule?.ExcludedShiftIds ?? new List<int>();
        foreach (var item in ExcludeShiftItems)
            item.IsChecked = excludedShiftIds.Contains(item.ShiftId);

        // 不與共事 — 勾選對應員工（已離職/不存在者自動剔除，來源清單已過濾）
        var excludedColleagueIds = notWithRule?.ExcludedColleagueIds ?? new List<int>();
        foreach (var item in NotWithItems)
            item.IsChecked = excludedColleagueIds.Contains(item.EmployeeId);

        IsEditing = true;
    }

    // ══════════════════════════════════════════════════════
    // 儲存
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public async Task SaveAsync()
    {
        // 檢查離職日後排班（預留）
        if (EditResignDate.HasValue && SelectedEmployee is not null)
        {
            var conflicts = await _employeeService.CheckScheduleAfterResignAsync(
                SelectedEmployee.Id, EditResignDate.Value);
            if (conflicts.Any())
            {
                ConflictMonths = conflicts;
                HasConflicts = true;
                return;
            }
        }

        await DoSaveAsync();
    }

    [RelayCommand]
    public async Task ForceSaveAsync()
    {
        HasConflicts = false;
        await DoSaveAsync();
    }

    private async Task DoSaveAsync()
    {
        var emp = SelectedEmployee ?? new Employee();
        emp.Name = EditName;
        emp.IdNumber = EditIdNumber;
        emp.Address = EditAddress;
        emp.Phone = EditPhone;
        emp.MessengerType = EditMessengerType;
        emp.MessengerValue = EditMessengerValue;
        emp.CustomContacts = EditCustomContacts;
        emp.DefaultShiftId = EditDefaultShift?.Id;
        emp.DefaultSalaryId = EditDefaultSalary?.Id;
        emp.DefaultBonuses = EditDefaultBonuses;
        emp.HireDate = EditHireDate;
        emp.ResignDate = EditResignDate;

        // ── 組裝排班規則 ──────────────────────────────────
        var rules = new List<ScheduleRule>();

        var checkedOffDays = FixedOffDayItems
            .Where(d => d.IsChecked)
            .Select(d => (int)d.Day)
            .ToList();
        if (checkedOffDays.Any())
            rules.Add(new ScheduleRule
            {
                EmployeeId = emp.Id,
                Type = ScheduleRuleType.FixedOff,
                FixedOffDays = checkedOffDays
            });

        var checkedShiftIds = ExcludeShiftItems
            .Where(s => s.IsChecked)
            .Select(s => s.ShiftId)
            .ToList();
        if (checkedShiftIds.Any())
            rules.Add(new ScheduleRule
            {
                EmployeeId = emp.Id,
                Type = ScheduleRuleType.ExcludeShift,
                ExcludedShiftIds = checkedShiftIds
            });

        var checkedColleagueIds = NotWithItems
            .Where(c => c.IsChecked)
            .Select(c => c.EmployeeId)
            .ToList();
        if (checkedColleagueIds.Any())
            rules.Add(new ScheduleRule
            {
                EmployeeId = emp.Id,
                Type = ScheduleRuleType.NotWith,
                ExcludedColleagueIds = checkedColleagueIds
            });

        emp.ScheduleRules = rules;

        // 儲存
        if (SelectedEmployee is null)
            await _employeeService.AddAsync(emp);
        else
            await _employeeService.UpdateAsync(emp);

        IsEditing = false;
        await LoadAsync();
        _snackbarService.Show("儲存成功", "員工資料已更新",
            ControlAppearance.Success, null, TimeSpan.FromSeconds(3));
    }

    // ══════════════════════════════════════════════════════
    // 刪除
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public async Task DeleteAsync(Employee emp)
    {
        var result = await _contentDialogService.ShowSimpleDialogAsync(
            new SimpleContentDialogCreateOptions
            {
                Title = "確認刪除",
                Content = $"確定要刪除員工「{emp.Name}」嗎？此操作無法復原。",
                PrimaryButtonText = "刪除",
                CloseButtonText = "取消",
            });

        if (result != ContentDialogResult.Primary)
            return;

        await _employeeService.DeleteAsync(emp.Id);
        await LoadAsync();
    }

    [RelayCommand]
    public void Cancel()
    {
        IsEditing = false;
        HasConflicts = false;
    }

    // ══════════════════════════════════════════════════════
    // 自訂聯絡方式
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public void AddCustomContact() =>
        EditCustomContacts = new List<CustomContact>(EditCustomContacts) { new() };

    [RelayCommand]
    public void RemoveCustomContact(CustomContact c)
    {
        var list = new List<CustomContact>(EditCustomContacts);
        list.Remove(c);
        EditCustomContacts = list;
    }

    // ══════════════════════════════════════════════════════
    // 預設獎金
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public void AddBonus() =>
        EditDefaultBonuses = new List<DefaultBonus>(EditDefaultBonuses) { new() };

    [RelayCommand]
    public void RemoveBonus(DefaultBonus b)
    {
        var list = new List<DefaultBonus>(EditDefaultBonuses);
        list.Remove(b);
        EditDefaultBonuses = list;
    }

    // ══════════════════════════════════════════════════════
    // 工具
    // ══════════════════════════════════════════════════════
    private void ClearEditFields()
    {
        EditName = string.Empty;
        EditIdNumber = string.Empty;
        EditAddress = string.Empty;
        EditPhone = string.Empty;
        EditMessengerType = null;
        EditMessengerValue = null;
        EditCustomContacts = new();
        EditDefaultShift = null;
        EditDefaultSalary = null;
        EditDefaultBonuses = new();
        EditHireDate = DateOnly.FromDateTime(DateTime.Today);
        EditResignDate = null;
        HasConflicts = false;
        ConflictMonths = new();

        // 清空排班規則勾選
        foreach (var d in FixedOffDayItems) d.IsChecked = false;
        foreach (var s in ExcludeShiftItems) s.IsChecked = false;
        foreach (var c in NotWithItems) c.IsChecked = false;
    }
}
