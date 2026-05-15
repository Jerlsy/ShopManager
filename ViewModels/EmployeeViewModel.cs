using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using ShopManager.Helpers;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.Views.Dialogs;
using System.Collections.ObjectModel;

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
    private readonly LineFollowerService _lineFollowerService;
    private readonly LineService _lineService;
    private readonly BankCodeService _bankCodeService;
    private readonly IAppSnackbarService _snackbarService;
    private readonly IAppDialogService _dialogService;

    private List<int> _shopClosedDays = new();

    public EmployeeViewModel(
        EmployeeService employeeService,
        ShiftSettingService shiftService,
        SalarySettingService salaryService,
        ShopSettingService shopSettingService,
        ScheduleConflictService conflictService,
        LineFollowerService lineFollowerService,
        LineService lineService,
        BankCodeService bankCodeService,
        IAppSnackbarService snackbarService,
        IAppDialogService dialogService)
    {
        _employeeService = employeeService;
        _shiftService = shiftService;
        _salaryService = salaryService;
        _shopSettingService = shopSettingService;
        _conflictService = conflictService;
        _lineFollowerService = lineFollowerService;
        _lineService = lineService;
        _bankCodeService = bankCodeService;
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

    public static IReadOnlyList<string> ColorPalette { get; } = App.EmployeeColorPalette;

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
    [ObservableProperty] private string _editColorHex = string.Empty;

    [ObservableProperty] private List<ContactInfo> _editContactInfos = new();

    // ── LINE 推播綁定 ──────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLineBinding))]
    private string? _editLineUserId;

    [ObservableProperty] private string? _editLineDisplayName;
    [ObservableProperty] private string? _editLinePictureUrl;

    public bool HasLineBinding => !string.IsNullOrEmpty(EditLineUserId);

    /// <summary>點擊綁定按鈕時觸發，View 負責開啟 LineFollowerWindow 並回傳 userId/displayName</summary>
    public event EventHandler? OpenLineBindingRequested;

    [RelayCommand]
    public void RequestLineBinding() => OpenLineBindingRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ClearLineBinding()
    {
        EditLineUserId = null;
        EditLineDisplayName = null;
    }

    public void ApplyLineBinding(string userId, string displayName, string? pictureUrl)
    {
        EditLineUserId = userId;
        EditLineDisplayName = displayName;
        EditLinePictureUrl = pictureUrl;
    }

    // ── 薪資戶 ────────────────────────────────────────────────
    public ObservableCollection<BankCode> BankCodes { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BankAccountSummary))]
    private BankCode? _editBankCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BankAccountSummary))]
    private string _editBankAccount = string.Empty;

    [ObservableProperty] private string _editBankAccountName = string.Empty;

    [ObservableProperty] private bool _isBankUpdating;

    public string BankAccountSummary
    {
        get
        {
            if (EditBankCode is null || string.IsNullOrWhiteSpace(EditBankAccount)) return "未設定";
            return $"{EditBankCode.Code} ****{EditBankAccount[^Math.Min(4, EditBankAccount.Length)..]}";
        }
    }

    [ObservableProperty] private SalarySetting? _editDefaultSalary;
    [ObservableProperty] private SalarySetting? _editHolidaySalary;
    [ObservableProperty] private DateOnly? _editInterviewDate;
    [ObservableProperty] private DateOnly _editHireDate = DateOnly.FromDateTime(DateTime.Today);
    [ObservableProperty] private DateOnly? _editResignDate;

    // ── 衝突狀態 ──────────────────────────────────────────
    [ObservableProperty] private bool _hasConflicts;
    [ObservableProperty] private List<string> _conflictMonths = new();

    // ── 靜態資料 ──────────────────────────────────────────
    public static List<string> EmployeeContactTypes { get; } = new()
    {
        "電話", "Email", "WhatsApp", "Telegram",
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
            return TaiwanIdValidator.Validate(EditIdNumber) ? null : "身分證格式不正確";
        }
    }
    public bool IsIdValid => IdNumberError == null;

    // ── 摘要顯示（對話框按鈕用） ──────────────────────────
    public string SalarySummary
    {
        get
        {
            if (EditDefaultSalary is null) return "未設定";
            if (EditDefaultSalary.Type == SalaryType.Hourly && EditHolidaySalary is not null)
                return $"平日 {EditDefaultSalary.Alias} ／ 假日 {EditHolidaySalary.Alias}";
            return EditDefaultSalary.Alias;
        }
    }

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

        if (BankCodes.Count == 0)
        {
            var banks = await _bankCodeService.GetAllAsync();
            foreach (var b in banks) BankCodes.Add(b);
        }
    }

    [RelayCommand]
    private async Task UpdateBankCodesAsync()
    {
        IsBankUpdating = true;
        try
        {
            var (success, message, _) = await _bankCodeService.UpdateFromWebAsync();
            if (success)
            {
                BankCodes.Clear();
                var banks = await _bankCodeService.GetAllAsync();
                foreach (var b in banks) BankCodes.Add(b);
                _snackbarService.ShowSuccess(message);
            }
            else
            {
                _snackbarService.ShowError(message);
            }
        }
        finally { IsBankUpdating = false; }
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
        EditColorHex = emp.ColorHex;

        if (emp.BirthDate.HasValue)
        {
            EditBirthYear  = emp.BirthDate.Value.Year;
            EditBirthMonth = emp.BirthDate.Value.Month;
            EditBirthDay   = emp.BirthDate.Value.Day;
        }

        EditContactInfos   = new List<ContactInfo>(emp.ContactInfos);
        EditLineUserId = emp.LineUserId;
        if (!string.IsNullOrEmpty(emp.LineUserId))
        {
            var follower = await _lineFollowerService.GetByEmployeeIdAsync(emp.Id);
            EditLineDisplayName = follower?.DisplayName;
            EditLinePictureUrl = follower?.PictureUrl;
        }
        else
        {
            EditLineDisplayName = null;
            EditLinePictureUrl = null;
        }
        EditBankCode        = BankCodes.FirstOrDefault(b => b.Code == emp.BankCode);
        EditBankAccount     = emp.BankAccount ?? string.Empty;
        EditBankAccountName = emp.BankAccountName ?? string.Empty;
        EditDefaultSalary  = AvailableSalaries.FirstOrDefault(s => s.Id == emp.DefaultSalaryId);
        EditHolidaySalary  = AvailableSalaries.FirstOrDefault(s => s.Id == emp.HolidaySalaryId);
        EditInterviewDate  = emp.InterviewDate;
        EditHireDate       = emp.HireDate == default ? DateOnly.FromDateTime(DateTime.Today) : emp.HireDate;
        EditResignDate     = emp.ResignDate;

        ApplyRulesToVm(emp.ScheduleRules);

        OnPropertyChanged(nameof(SalarySummary));
        OnPropertyChanged(nameof(EmploymentSummary));
        OnPropertyChanged(nameof(ScheduleRulesSummary));
        OnPropertyChanged(nameof(BankAccountSummary));
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
        var dialog = new SalarySettingDialog(AvailableSalaries, EditDefaultSalary, EditHolidaySalary);
        var result = await DialogHost.Show(dialog, "RootDialog");
        if (result is SalaryDialogResult r)
        {
            EditDefaultSalary = r.WeekdayPlan;
            EditHolidaySalary = r.HolidayPlan;
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
    // 對話框：薪資帳戶
    // ══════════════════════════════════════════════════════
    [RelayCommand]
    private async Task OpenBankAccountDialogAsync()
    {
        var dialog = new Views.Dialogs.BankAccountDialog(
            BankCodes.ToList(), EditBankCode, EditBankAccount, EditBankAccountName);
        var result = await DialogHost.Show(dialog, "RootDialog");
        if (result is Views.Dialogs.BankAccountDialogResult r)
        {
            EditBankCode        = r.Bank;
            EditBankAccount     = r.Account;
            EditBankAccountName = r.AccountName;
            OnPropertyChanged(nameof(BankAccountSummary));
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
        var oldLineUserId = SelectedEmployee?.LineUserId;
        var emp = SelectedEmployee ?? new Employee();
        emp.Name         = EditName;
        emp.EnglishName  = string.IsNullOrWhiteSpace(EditEnglishName) ? null : EditEnglishName.Trim();
        emp.IdNumber     = EditIdNumber;
        emp.AvatarPhotoData = EditAvatarPhotoData;
        emp.BirthDate    = (EditBirthYear.HasValue && EditBirthMonth.HasValue && EditBirthDay.HasValue)
            ? new DateOnly(EditBirthYear.Value, EditBirthMonth.Value, EditBirthDay.Value)
            : null;
        emp.ContactInfos    = EditContactInfos;
        emp.LineUserId      = string.IsNullOrEmpty(EditLineUserId) ? null : EditLineUserId;
        emp.BankCode        = EditBankCode?.Code;
        emp.BankAccount     = string.IsNullOrWhiteSpace(EditBankAccount) ? null : EditBankAccount.Replace("-", "").Replace(" ", "");
        emp.BankAccountName = string.IsNullOrWhiteSpace(EditBankAccountName) ? null : EditBankAccountName.Trim();
        emp.DefaultSalaryId = EditDefaultSalary?.Id;
        emp.HolidaySalaryId = (EditDefaultSalary?.Type == SalaryType.Hourly) ? EditHolidaySalary?.Id : null;
        emp.InterviewDate   = EditInterviewDate;
        emp.HireDate        = EditHireDate;
        emp.ResignDate      = EditResignDate;

        emp.ScheduleRules = BuildRulesFromVm(emp.Id);

        bool isUpdate = SelectedEmployee is not null;
        emp.ColorHex = !string.IsNullOrEmpty(EditColorHex)
            ? EditColorHex
            : App.EmployeeColorPalette[Employees.Count % App.EmployeeColorPalette.Length];
        if (!isUpdate)
            await _employeeService.AddAsync(emp);
        else
            await _employeeService.UpdateAsync(emp);

        // 同步 LineFollower 綁定狀態
        if (!string.IsNullOrEmpty(emp.LineUserId))
            await _lineFollowerService.BindAsync(emp.LineUserId, emp.Id);
        else if (!string.IsNullOrEmpty(oldLineUserId))
            await _lineFollowerService.UnbindAsync(emp.Id);

        // 新綁定時發送歡迎訊息
        bool isNewBinding = !string.IsNullOrEmpty(emp.LineUserId) && emp.LineUserId != oldLineUserId;
        if (isNewBinding)
        {
            var shopSetting = await _shopSettingService.GetAsync();
            var token = shopSetting?.LineChannelAccessToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                var welcomeMsg = string.IsNullOrWhiteSpace(shopSetting?.LineWelcomeMessage)
                    ? "✅ 綁定成功！您的 LINE 帳號已與店鋪排班系統連結，後續班表通知將透過此帳號發送。"
                    : shopSetting.LineWelcomeMessage.Replace("{name}", EditLineDisplayName ?? emp.Name);
                await _lineService.PushMessageAsync(token, emp.LineUserId, welcomeMsg);
            }
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
        EditColorHex = App.EmployeeColorPalette[Employees.Count % App.EmployeeColorPalette.Length];
        EditBirthYear      = null;
        EditBirthMonth     = null;
        EditBirthDay       = null;
        EditContactInfos    = new();
        EditLineUserId      = null;
        EditLineDisplayName = null;
        EditLinePictureUrl  = null;
        EditBankCode        = null;
        EditBankAccount     = string.Empty;
        EditBankAccountName = string.Empty;
        EditDefaultSalary  = null;
        EditHolidaySalary  = null;
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
        OnPropertyChanged(nameof(BankAccountSummary));
    }

    private void ApplyRulesToVm(IEnumerable<ScheduleRule> rules)
    {
        var lookup = rules.ToDictionary(r => r.Type);

        var fixedOffDays = lookup.TryGetValue(ScheduleRuleType.FixedOff,      out var fo)  ? fo.FixedOffDays          : new();
        var excludedIds  = lookup.TryGetValue(ScheduleRuleType.ExcludeShift,  out var ex)  ? ex.ExcludedShiftIds      : new();
        var notWithIds   = lookup.TryGetValue(ScheduleRuleType.NotWith,       out var nw)  ? nw.ExcludedColleagueIds  : new();
        var notWithDayIds= lookup.TryGetValue(ScheduleRuleType.NotWithDay,    out var nwd) ? nwd.ExcludedColleagueIds : new();

        foreach (var item in FixedOffDayItems)  item.IsChecked = fixedOffDays.Contains((int)item.Day);
        foreach (var item in ExcludeShiftItems) item.IsChecked = excludedIds.Contains(item.ShiftId);
        foreach (var item in NotWithItems)      item.IsChecked = notWithIds.Contains(item.EmployeeId);
        foreach (var item in NotWithDayItems)   item.IsChecked = notWithDayIds.Contains(item.EmployeeId);
    }

    private List<ScheduleRule> BuildRulesFromVm(int empId)
    {
        var rules = new List<ScheduleRule>();

        var offDays = FixedOffDayItems.Where(d => d.IsChecked).Select(d => (int)d.Day).ToList();
        if (offDays.Any())
            rules.Add(new ScheduleRule { EmployeeId = empId, Type = ScheduleRuleType.FixedOff, FixedOffDays = offDays });

        var excludedShifts = ExcludeShiftItems.Where(s => s.IsChecked).Select(s => s.ShiftId).ToList();
        if (excludedShifts.Any())
            rules.Add(new ScheduleRule { EmployeeId = empId, Type = ScheduleRuleType.ExcludeShift, ExcludedShiftIds = excludedShifts });

        var notWithIds = NotWithItems.Where(e => e.IsChecked).Select(e => e.EmployeeId).ToList();
        if (notWithIds.Any())
            rules.Add(new ScheduleRule { EmployeeId = empId, Type = ScheduleRuleType.NotWith, ExcludedColleagueIds = notWithIds });

        var notWithDayIds = NotWithDayItems.Where(e => e.IsChecked).Select(e => e.EmployeeId).ToList();
        if (notWithDayIds.Any())
            rules.Add(new ScheduleRule { EmployeeId = empId, Type = ScheduleRuleType.NotWithDay, ExcludedColleagueIds = notWithDayIds });

        return rules;
    }
}
