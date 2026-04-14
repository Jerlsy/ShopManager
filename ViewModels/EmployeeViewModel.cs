using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;

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
    private readonly IAppSnackbarService _snackbarService;
    private readonly IAppDialogService _dialogService;

    public EmployeeViewModel(EmployeeService employeeService,
        ShiftSettingService shiftService, SalarySettingService salaryService,
        IAppSnackbarService snackbarService, IAppDialogService dialogService)
    {
        _employeeService = employeeService;
        _shiftService = shiftService;
        _salaryService = salaryService;
        _snackbarService = snackbarService;
        _dialogService = dialogService;

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
    public List<DayOfWeekItem> FixedOffDayItems { get; }
    [ObservableProperty] private List<ShiftCheckItem> _excludeShiftItems = new();
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

    private async Task LoadScheduleRuleSourcesAsync(int? currentEmployeeId = null)
    {
        var shifts = await _shiftService.GetAllAsync();
        ExcludeShiftItems = shifts
            .Where(s => s.IsEnabled)
            .Select(s => new ShiftCheckItem { ShiftId = s.Id, Alias = s.Alias })
            .ToList();

        var allActive = Employees.Where(e =>
            !e.IsResigned && e.Id != (currentEmployeeId ?? -1)).ToList();

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

        var fixedOffRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.FixedOff);
        var excludeShiftRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.ExcludeShift);
        var notWithRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.NotWith);

        var fixedOffDays = fixedOffRule?.FixedOffDays ?? new List<int>();
        foreach (var item in FixedOffDayItems)
            item.IsChecked = fixedOffDays.Contains((int)item.Day);

        var excludedShiftIds = excludeShiftRule?.ExcludedShiftIds ?? new List<int>();
        foreach (var item in ExcludeShiftItems)
            item.IsChecked = excludedShiftIds.Contains(item.ShiftId);

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

        var rules = new List<ScheduleRule>();

        var checkedOffDays = FixedOffDayItems.Where(d => d.IsChecked)
            .Select(d => (int)d.Day).ToList();
        if (checkedOffDays.Any())
            rules.Add(new ScheduleRule
            {
                EmployeeId = emp.Id,
                Type = ScheduleRuleType.FixedOff,
                FixedOffDays = checkedOffDays
            });

        var checkedShiftIds = ExcludeShiftItems.Where(s => s.IsChecked)
            .Select(s => s.ShiftId).ToList();
        if (checkedShiftIds.Any())
            rules.Add(new ScheduleRule
            {
                EmployeeId = emp.Id,
                Type = ScheduleRuleType.ExcludeShift,
                ExcludedShiftIds = checkedShiftIds
            });

        var checkedColleagueIds = NotWithItems.Where(c => c.IsChecked)
            .Select(c => c.EmployeeId).ToList();
        if (checkedColleagueIds.Any())
            rules.Add(new ScheduleRule
            {
                EmployeeId = emp.Id,
                Type = ScheduleRuleType.NotWith,
                ExcludedColleagueIds = checkedColleagueIds
            });

        emp.ScheduleRules = rules;

        if (SelectedEmployee is null) await _employeeService.AddAsync(emp);
        else await _employeeService.UpdateAsync(emp);

        IsEditing = false;
        await LoadAsync();
        _snackbarService.ShowSuccess("員工資料已儲存");
    }

    // ══════════════════════════════════════════════════════
    // 刪除
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public async Task DeleteAsync(Employee emp)
    {
        var confirmed = await _dialogService.ShowConfirmAsync(
            "確認刪除",
            $"確定要刪除員工「{emp.Name}」嗎？此操作無法復原。",
            "刪除", "取消");

        if (!confirmed) return;

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
    // 自訂聯絡方式 / 預設獎金
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
        foreach (var d in FixedOffDayItems) d.IsChecked = false;
        foreach (var s in ExcludeShiftItems) s.IsChecked = false;
        foreach (var c in NotWithItems) c.IsChecked = false;
    }
}
