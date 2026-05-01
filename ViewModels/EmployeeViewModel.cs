using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.Views.Dialogs;

namespace ShopManager.ViewModels;

// ── 排班規則 UI 模型 ────────────────────────────────────────

public partial class DayOfWeekItem : ObservableObject
{
    public DayOfWeek Day { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsShopClosed { get; init; }
    public bool IsUserEditable => !IsShopClosed;
    [ObservableProperty] private bool _isChecked;
}

public partial class AvailableDayItem : ObservableObject
{
    public DayOfWeek Day { get; init; }
    public string Label { get; init; } = string.Empty;
    [ObservableProperty] private bool _isAvailable;
    public bool IsShopClosed { get; init; }
}

public partial class ShiftCheckItem : ObservableObject
{
    public int ShiftId { get; init; }
    public string Alias { get; init; } = string.Empty;
    [ObservableProperty] private bool _isChecked;
}

public partial class PreferShiftItem : ObservableObject
{
    public int? ShiftId { get; init; }   // null = 不限
    public string Label { get; init; } = string.Empty;
    [ObservableProperty] private bool _isSelected;
    public bool IsUnlimited => ShiftId is null;
}

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
    private readonly ShopSettingService _shopSettingService;
    private readonly ScheduleConflictService _conflictService;
    private readonly IAppSnackbarService _snackbarService;
    private readonly IAppDialogService _dialogService;

    private List<int> _shopClosedDays = new();

    public EmployeeViewModel(
        EmployeeService employeeService,
        ShiftSettingService shiftService,
        SalarySettingService salaryService,
        ShopSettingService shopSettingService,
        ScheduleConflictService conflictService,
        IAppSnackbarService snackbarService,
        IAppDialogService dialogService)
    {
        _employeeService = employeeService;
        _shiftService = shiftService;
        _salaryService = salaryService;
        _shopSettingService = shopSettingService;
        _conflictService = conflictService;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    private List<Employee> _employees = new();

    [ObservableProperty] private Employee? _selectedEmployee;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    [NotifyPropertyChangedFor(nameof(ShowListCards))]
    private bool _isEditing;

    public bool ShowListCards => !IsEditing;

    public bool ShowEmptyHint => !IsEditing && Employees.Count == 0;

    // ── 下拉選單來源 ───────────────────────────────────────
    [ObservableProperty] private List<ShiftSetting> _availableShifts = new();
    [ObservableProperty] private List<SalarySetting> _availableSalaries = new();

    // ── 排班規則 UI ────────────────────────────────────────
    public List<DayOfWeekItem> FixedOffDayItems { get; }
    [ObservableProperty] private List<ShiftCheckItem> _excludeShiftItems = new();
    [ObservableProperty] private List<ColleagueCheckItem> _notWithItems = new();
    [ObservableProperty] private List<ColleagueCheckItem> _notWithDayItems = new();
    public bool HasNoColleagues => NotWithItems.Count == 0 && NotWithDayItems.Count == 0;

    // ── 基本編輯欄位 ───────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdNumberError))]
    [NotifyPropertyChangedFor(nameof(IsIdValid))]
    private string _editName = string.Empty;

    [ObservableProperty] private string _editEnglishName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IdNumberError))]
    [NotifyPropertyChangedFor(nameof(IsIdValid))]
    private string _editIdNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BirthDays))]
    private int? _editBirthYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BirthDays))]
    private int? _editBirthMonth;

    [ObservableProperty] private int? _editBirthDay;

    [ObservableProperty] private byte[]? _editAvatarPhotoData;

    [ObservableProperty] private List<ContactInfo> _editContactInfos = new();

    [ObservableProperty] private SalarySetting? _editDefaultSalary;
    [ObservableProperty] private DateOnly? _editInterviewDate;
    [ObservableProperty] private DateOnly _editHireDate = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private DateOnly? _editResignDate;

    // ── 衝突狀態 ──────────────────────────────────────────
    [ObservableProperty] private bool _hasConflicts;
    [ObservableProperty] private List<string> _conflictMonths = new();

    // ── 靜態資料 ──────────────────────────────────────────
    public static List<string> EmployeeContactTypes { get; } = new()
    {
        "電話", "Email", "Line", "WhatsApp", "Telegram",
        "WeChat", "Facebook", "Instagram", "其他"
    };

    public static List<int> BirthYears { get; } =
        Enumerable.Range(1940, DateTime.Today.Year - 1940 + 1).Reverse().ToList();

    public static List<int> BirthMonths { get; } = Enumerable.Range(1, 12).ToList();

    public List<int> BirthDays =>
        (EditBirthYear.HasValue && EditBirthMonth.HasValue)
            ? Enumerable.Range(1, DateTime.DaysInMonth(EditBirthYear.Value, EditBirthMonth.Value)).ToList()
            : Enumerable.Range(1, 31).ToList();

    // ── 身分證驗證 ────────────────────────────────────────
    public string? IdNumberError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(EditIdNumber)) return "身分證為必填";
            return ValidateTaiwanId(EditIdNumber) ? null : "身分證格式不正確";
        }
    }
    public bool IsIdValid => IdNumberError == null;

    private static readonly Dictionary<char, int> _idLetterMap = new()
    {
        {'A',10},{'B',11},{'C',12},{'D',13},{'E',14},{'F',15},{'G',16},{'H',17},
        {'I',34},{'J',18},{'K',19},{'L',20},{'M',21},{'N',22},{'O',35},{'P',23},
        {'Q',24},{'R',25},{'S',26},{'T',27},{'U',28},{'V',29},{'W',32},{'X',30},
        {'Y',31},{'Z',33}
    };

    public static bool ValidateTaiwanId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        id = id.Trim().ToUpper();
        if (id.Length != 10) return false;
        if (!_idLetterMap.TryGetValue(id[0], out int code)) return false;
        for (int i = 1; i < 10; i++)
            if (!char.IsDigit(id[i])) return false;
        int[] weights = { 1, 9, 8, 7, 6, 5, 4, 3, 2, 1, 1 };
        int[] digits = new int[11];
        digits[0] = code / 10;
        digits[1] = code % 10;
        for (int i = 2; i <= 10; i++) digits[i] = id[i - 1] - '0';
        return digits.Zip(weights, (d, w) => d * w).Sum() % 10 == 0;
    }

    // ── 摘要顯示（對話框按鈕用） ──────────────────────────
    public string SalarySummary =>
        EditDefaultSalary is null ? "未設定" : EditDefaultSalary.Alias;

    public string ScheduleRulesSummary
    {
        get
        {
            var parts = new List<string>();
            var offDays = FixedOffDayItems.Where(d => d.IsChecked).ToList();
            if (offDays.Any()) parts.Add($"休 {string.Join("", offDays.Select(d => d.Label))}");
            var excCount = ExcludeShiftItems.Count(s => s.IsChecked);
            if (excCount > 0) parts.Add($"排除 {excCount} 個班別");
            var nwCount = NotWithItems.Count(e => e.IsChecked);
            if (nwCount > 0) parts.Add($"不與 {nwCount} 人同班");
            var nwdCount = NotWithDayItems.Count(e => e.IsChecked);
            if (nwdCount > 0) parts.Add($"不與 {nwdCount} 人同天");
            return parts.Count == 0 ? "未設定" : string.Join("・", parts);
        }
    }

    public string EmploymentSummary
    {
        get
        {
            if (EditHireDate == default) return "未設定到職日";
            var s = EditHireDate.ToString("yyyy/MM/dd") + " 到職";
            if (EditInterviewDate.HasValue)
                s = EditInterviewDate.Value.ToString("yyyy/MM/dd") + " 面試 · " + s;
            if (EditResignDate.HasValue)
                s += " · " + EditResignDate.Value.ToString("yyyy/MM/dd") + " 離職";
            return s;
        }
    }

    // ══════════════════════════════════════════════════════
    // 載入資料
    // ══════════════════════════════════════════════════════
    public async Task LoadAsync()
    {
        Employees = await _employeeService.GetAllAsync();
        AvailableShifts = (await _shiftService.GetAllAsync()).Where(s => s.IsEnabled).ToList();
        AvailableSalaries = await _salaryService.GetAllAsync();
        var shopSetting = await _shopSettingService.GetAsync();
        _shopClosedDays = shopSetting?.ClosedDaysOfWeek ?? new List<int>();
    }

    private async Task LoadScheduleRuleSourcesAsync(int? currentEmployeeId = null)
    {
        var shifts = await _shiftService.GetAllAsync();
        ExcludeShiftItems = shifts
            .Where(s => s.IsEnabled)
            .Select(s => new ShiftCheckItem { ShiftId = s.Id, Alias = s.Alias })
            .ToList();

        var colleagues = Employees
            .Where(e => !e.IsResigned && e.Id != (currentEmployeeId ?? -1))
            .Select(e => new ColleagueCheckItem { EmployeeId = e.Id, Name = e.Name })
            .ToList();

        NotWithItems    = colleagues.Select(e => new ColleagueCheckItem { EmployeeId = e.EmployeeId, Name = e.Name }).ToList();
        NotWithDayItems = colleagues.Select(e => new ColleagueCheckItem { EmployeeId = e.EmployeeId, Name = e.Name }).ToList();

        OnPropertyChanged(nameof(HasNoColleagues));
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
        EditEnglishName = emp.EnglishName ?? string.Empty;
        EditIdNumber = emp.IdNumber;
        EditAvatarPhotoData = emp.AvatarPhotoData;

        if (emp.BirthDate.HasValue)
        {
            EditBirthYear  = emp.BirthDate.Value.Year;
            EditBirthMonth = emp.BirthDate.Value.Month;
            EditBirthDay   = emp.BirthDate.Value.Day;
        }

        EditContactInfos   = new List<ContactInfo>(emp.ContactInfos);
        EditDefaultSalary  = AvailableSalaries.FirstOrDefault(s => s.Id == emp.DefaultSalaryId);
        EditInterviewDate  = emp.InterviewDate;
        EditHireDate       = emp.HireDate == default ? DateOnly.FromDateTime(DateTime.Today) : emp.HireDate;
        EditResignDate     = emp.ResignDate;

        // 固定休假
        var fixedOffRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.FixedOff);
        var fixedOffDays = fixedOffRule?.FixedOffDays ?? new List<int>();
        foreach (var item in FixedOffDayItems)
            item.IsChecked = fixedOffDays.Contains((int)item.Day);

        // 排除班別
        var excludeRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.ExcludeShift);
        var excludedIds = excludeRule?.ExcludedShiftIds ?? new List<int>();
        foreach (var item in ExcludeShiftItems)
            item.IsChecked = excludedIds.Contains(item.ShiftId);

        // 不與同班
        var notWithRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.NotWith);
        var notWithIds  = notWithRule?.ExcludedColleagueIds ?? new List<int>();
        foreach (var item in NotWithItems)
            item.IsChecked = notWithIds.Contains(item.EmployeeId);

        // 不與同天
        var notWithDayRule = emp.ScheduleRules.FirstOrDefault(r => r.Type == ScheduleRuleType.NotWithDay);
        var notWithDayIds  = notWithDayRule?.ExcludedColleagueIds ?? new List<int>();
        foreach (var item in NotWithDayItems)
            item.IsChecked = notWithDayIds.Contains(item.EmployeeId);

        OnPropertyChanged(nameof(SalarySummary));
        OnPropertyChanged(nameof(EmploymentSummary));
        OnPropertyChanged(nameof(ScheduleRulesSummary));
        IsEditing = true;
    }

    // ══════════════════════════════════════════════════════
    // 大頭貼
    // ══════════════════════════════════════════════════════
    public void SetAvatarPhoto(byte[] data)
    {
        EditAvatarPhotoData = data;
    }

    // ══════════════════════════════════════════════════════
    // 對話框：薪資設定
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    private async Task OpenSalaryDialogAsync()
    {
        var dialog = new SalarySettingDialog(AvailableSalaries, EditDefaultSalary);
        var result = await DialogHost.Show(dialog, "RootDialog");
        if (result is SalaryDialogResult r)
        {
            EditDefaultSalary = r.Plan;
            OnPropertyChanged(nameof(SalarySummary));
        }
    }

    // ══════════════════════════════════════════════════════
    // 對話框：到職設定
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    private async Task OpenEmploymentDialogAsync()
    {
        var dialog = new EmploymentDialog(EditInterviewDate, EditHireDate, EditResignDate);
        var result = await DialogHost.Show(dialog, "RootDialog");
        if (result is EmploymentDialogResult r)
        {
            EditInterviewDate = r.InterviewDate;
            EditHireDate      = r.HireDate;
            EditResignDate    = r.ResignDate;
            OnPropertyChanged(nameof(EmploymentSummary));
        }
    }

    // ══════════════════════════════════════════════════════
    // 對話框：排班規則
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    private async Task OpenScheduleRulesDialogAsync()
    {
        var dialog = new Views.Dialogs.ScheduleRulesDialog(FixedOffDayItems, ExcludeShiftItems, NotWithItems, NotWithDayItems, _shopClosedDays);
        var result = await DialogHost.Show(dialog, "RootDialog");
        if (result is Views.Dialogs.ScheduleRulesDialogResult r)
        {
            foreach (var d in FixedOffDayItems)
                d.IsChecked = r.FixedOffDays.Contains((int)d.Day);
            foreach (var s in ExcludeShiftItems)
                s.IsChecked = r.ExcludedShiftIds.Contains(s.ShiftId);
            foreach (var e in NotWithItems)
                e.IsChecked = r.NotWithEmployeeIds.Contains(e.EmployeeId);
            foreach (var e in NotWithDayItems)
                e.IsChecked = r.NotWithDayEmployeeIds.Contains(e.EmployeeId);
            OnPropertyChanged(nameof(ScheduleRulesSummary));
        }
    }

    // ══════════════════════════════════════════════════════
    // 儲存
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditName))
        {
            _snackbarService.ShowError("姓名為必填");
            return;
        }
        if (!IsIdValid)
        {
            _snackbarService.ShowError(IdNumberError!);
            return;
        }

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
        emp.Name         = EditName;
        emp.EnglishName  = string.IsNullOrWhiteSpace(EditEnglishName) ? null : EditEnglishName.Trim();
        emp.IdNumber     = EditIdNumber;
        emp.AvatarPhotoData = EditAvatarPhotoData;
        emp.BirthDate    = (EditBirthYear.HasValue && EditBirthMonth.HasValue && EditBirthDay.HasValue)
            ? new DateOnly(EditBirthYear.Value, EditBirthMonth.Value, EditBirthDay.Value)
            : null;
        emp.ContactInfos    = EditContactInfos;
        emp.DefaultSalaryId = EditDefaultSalary?.Id;
        emp.InterviewDate   = EditInterviewDate;
        emp.HireDate        = EditHireDate;
        emp.ResignDate      = EditResignDate;

        // 排班規則
        var rules = new List<ScheduleRule>();

        var offDays = FixedOffDayItems.Where(d => d.IsChecked).Select(d => (int)d.Day).ToList();
        if (offDays.Any())
            rules.Add(new ScheduleRule { EmployeeId = emp.Id, Type = ScheduleRuleType.FixedOff, FixedOffDays = offDays });

        var excludedShifts = ExcludeShiftItems.Where(s => s.IsChecked).Select(s => s.ShiftId).ToList();
        if (excludedShifts.Any())
            rules.Add(new ScheduleRule { EmployeeId = emp.Id, Type = ScheduleRuleType.ExcludeShift, ExcludedShiftIds = excludedShifts });

        var notWithIds = NotWithItems.Where(e => e.IsChecked).Select(e => e.EmployeeId).ToList();
        if (notWithIds.Any())
            rules.Add(new ScheduleRule { EmployeeId = emp.Id, Type = ScheduleRuleType.NotWith, ExcludedColleagueIds = notWithIds });

        var notWithDayIds = NotWithDayItems.Where(e => e.IsChecked).Select(e => e.EmployeeId).ToList();
        if (notWithDayIds.Any())
            rules.Add(new ScheduleRule { EmployeeId = emp.Id, Type = ScheduleRuleType.NotWithDay, ExcludedColleagueIds = notWithDayIds });

        emp.ScheduleRules = rules;

        bool isUpdate = SelectedEmployee is not null;
        if (!isUpdate)
        {
            emp.ColorHex = App.EmployeeColorPalette[Employees.Count % App.EmployeeColorPalette.Length];
            await _employeeService.AddAsync(emp);
        }
        else
        {
            await _employeeService.UpdateAsync(emp);
        }

        SelectedEmployee = null;
        IsEditing = false;
        await LoadAsync();
        _snackbarService.ShowSuccess("員工資料已儲存");

        // 員工規則/離職日變更 → 重新檢查所有含此員工的班表
        if (isUpdate)
        {
            var conflictCount = await _conflictService.RecheckByEmployeeAsync(emp.Id);
            if (conflictCount > 0)
                _snackbarService.ShowWarning($"儲存後發現 {conflictCount} 條排班衝突，請至排班頁面調整");
        }
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
        SelectedEmployee = null;
        IsEditing = false;
        HasConflicts = false;
    }

    // ══════════════════════════════════════════════════════
    // 聯絡方式
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    public void AddContactInfo() =>
        EditContactInfos = new List<ContactInfo>(EditContactInfos)
        {
            new() { Type = "電話" }
        };

    [RelayCommand]
    public void RemoveContactInfo(ContactInfo c)
    {
        var list = new List<ContactInfo>(EditContactInfos);
        list.Remove(c);
        EditContactInfos = list;
    }

    // ══════════════════════════════════════════════════════
    // 清除欄位
    // ══════════════════════════════════════════════════════
    private void ClearEditFields()
    {
        EditName           = string.Empty;
        EditEnglishName    = string.Empty;
        EditIdNumber       = string.Empty;
        EditAvatarPhotoData = null;
        EditBirthYear      = null;
        EditBirthMonth     = null;
        EditBirthDay       = null;
        EditContactInfos   = new();
        EditDefaultSalary  = null;
        EditInterviewDate  = null;
        EditHireDate       = DateOnly.FromDateTime(DateTime.Today);
        EditResignDate     = null;
        HasConflicts       = false;
        ConflictMonths     = new();

        foreach (var d in FixedOffDayItems)   d.IsChecked = false;
        foreach (var s in ExcludeShiftItems)  s.IsChecked = false;
        foreach (var e in NotWithItems)       e.IsChecked = false;
        foreach (var e in NotWithDayItems)    e.IsChecked = false;

        OnPropertyChanged(nameof(SalarySummary));
        OnPropertyChanged(nameof(EmploymentSummary));
        OnPropertyChanged(nameof(ScheduleRulesSummary));
    }
}
