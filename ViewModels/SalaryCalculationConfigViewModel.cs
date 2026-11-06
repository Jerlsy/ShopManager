using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace ShopManager.ViewModels;

public partial class SalaryCalculationConfigViewModel : ObservableObject
{
    private readonly Dictionary<int, List<DateOnly>> _employeeDates;
    private readonly MonthlySchedule _schedule;
    private readonly List<Employee> _activeEmployees;

    public List<WeekdayCheckItem> WeekdayItems { get; }
    public List<Employee> ScheduledEmployees { get; }

    [ObservableProperty] private bool _includeNationalHolidays = true;

    public ObservableCollection<DailyOverrideItem> Overrides { get; } = new();

    // ── 打卡資料 ─────────────────────────────────────────────
    [ObservableProperty] private int _lateGraceMinutes = 10;
    [ObservableProperty] private int _earlyLeaveGraceMinutes = 10;

    [ObservableProperty] private bool _hasAttendance;
    [ObservableProperty] private string _importFileName = string.Empty;
    /// <summary>資料來源說明：已保存的匯入時間，或新匯入待保存</summary>
    [ObservableProperty] private string _importSourceText = string.Empty;
    [ObservableProperty] private string _importSummary = string.Empty;
    [ObservableProperty] private string _importError = string.Empty;
    [ObservableProperty] private string _pendingDeleteNote = string.Empty;

    private readonly Guid _shopId;
    private StoredAttendanceFile? _storedFile;   // 開啟對話框時該月已保存的打卡檔
    private byte[]? _pendingContent;              // 本次新匯入、尚未保存的檔案內容
    private bool _deleteStored;                   // 本次按了刪除，確定時移除已保存的檔案

    /// <summary>打卡檔裡找不到對應員工的名字，讓業主手動指定</summary>
    public ObservableCollection<ClockNameMappingItem> UnmatchedNames { get; } = new();

    // 已自動對應到員工的打卡（員工 Id → 打卡）
    private Dictionary<int, List<ClockPunch>> _matchedPunches = new();

    public SalaryCalculationConfigViewModel(
        Guid shopId,
        MonthlySchedule schedule,
        List<Employee> employees,
        List<Employee> activeEmployees,
        List<int> shopClosedDaysOfWeek,
        SalaryCalculationConfig? lastConfig = null)
    {
        _shopId          = shopId;
        _schedule        = schedule;
        _activeEmployees = activeEmployees;

        // 假日星期選項（不含店休日），預填上次勾選
        WeekdayItems = new List<WeekdayCheckItem>
        {
            new() { Day = DayOfWeek.Monday,    Label = "週一", IsShopClosed = shopClosedDaysOfWeek.Contains(1) },
            new() { Day = DayOfWeek.Tuesday,   Label = "週二", IsShopClosed = shopClosedDaysOfWeek.Contains(2) },
            new() { Day = DayOfWeek.Wednesday, Label = "週三", IsShopClosed = shopClosedDaysOfWeek.Contains(3) },
            new() { Day = DayOfWeek.Thursday,  Label = "週四", IsShopClosed = shopClosedDaysOfWeek.Contains(4) },
            new() { Day = DayOfWeek.Friday,    Label = "週五", IsShopClosed = shopClosedDaysOfWeek.Contains(5) },
            new() { Day = DayOfWeek.Saturday,  Label = "週六", IsChecked=true, IsShopClosed = shopClosedDaysOfWeek.Contains(6) },
            new() { Day = DayOfWeek.Sunday,    Label = "週日", IsChecked=true, IsShopClosed = shopClosedDaysOfWeek.Contains(0) },
        };

        if (lastConfig is not null)
        {
            foreach (var item in WeekdayItems)
                item.IsChecked = lastConfig.HolidayDaysOfWeek.Contains(item.Day);
            IncludeNationalHolidays = lastConfig.IncludeNationalHolidays;
            LateGraceMinutes        = lastConfig.LateGraceMinutes;
            EarlyLeaveGraceMinutes  = lastConfig.EarlyLeaveGraceMinutes;
        }

        // 各員工有排班的日期清單
        _employeeDates = schedule.Entries
            .Where(e => e.ShiftSetting is not null)
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Date).Distinct().OrderBy(d => d).ToList());

        ScheduledEmployees = employees
            .Where(e => _employeeDates.ContainsKey(e.Id))
            .OrderBy(e => e.Name)
            .ToList();

        // 該月之前匯入過打卡檔就自動沿用
        TryLoadStored();
    }

    [RelayCommand]
    private void AddOverride()
    {
        Overrides.Add(new DailyOverrideItem(ScheduledEmployees, _employeeDates));
    }

    [RelayCommand]
    private void RemoveOverride(DailyOverrideItem item)
    {
        Overrides.Remove(item);
    }

    /// <summary>從 iCHEF 後台自動下載當月打卡檔；無法自動下載時改走手動選檔</summary>
    [RelayCommand]
    private void DownloadFromIchef()
    {
        var owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>()
                        .FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current.MainWindow;
        var win = new Views.Dialogs.IchefDownloadWindow(_shopId, _schedule.Year, _schedule.Month) { Owner = owner };
        win.ShowDialog();
        var result = win.Result;

        switch (result.Status)
        {
            case Views.Dialogs.IchefDownloadStatus.Cancelled:
                return;

            case Views.Dialogs.IchefDownloadStatus.Empty:
                ImportError = result.Message ?? string.Empty;
                return;

            case Views.Dialogs.IchefDownloadStatus.Success:
                if (LoadAttendance(result.Content!, out var error))
                {
                    _pendingContent  = result.Content;
                    _deleteStored    = false;
                    ImportFileName   = $"iCHEF 打卡紀錄 {_schedule.Year}-{_schedule.Month:D2}.xlsx";
                    ImportSourceText = _storedFile is null
                        ? "剛從 iCHEF 後台下載，按「確定並計算」後保存"
                        : "剛從 iCHEF 後台下載，按「確定並計算」後覆蓋已保存的檔案";
                    return;
                }
                // 下載到了但內容用不了：當作自動下載失敗，往下走手動選檔
                ImportError = $"自動下載的檔案無法使用（{error}），請手動選擇檔案";
                break;

            default:
                ImportError = $"自動下載失敗：{result.Message}。請手動選擇打卡檔案";
                break;
        }

        ImportFromFile(keepError: true);
    }

    [RelayCommand]
    private void ImportAttendance() => ImportFromFile(keepError: false);

    private void ImportFromFile(bool keepError)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "選擇 POS 匯出的打卡紀錄",
            Filter = "Excel 檔案 (*.xlsx)|*.xlsx",
        };
        if (dlg.ShowDialog() != true) return;

        if (!keepError) ImportError = string.Empty;

        byte[] content;
        try
        {
            // 允許讀取 Excel 正開著的檔案；整份讀進記憶體，按「確定並計算」時再另存
            using var fs = new FileStream(dlg.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            content = ms.ToArray();
        }
        catch (Exception ex)
        {
            ImportError = $"無法讀取檔案：{ex.Message}";
            return;
        }

        if (!LoadAttendance(content, out var error))
        {
            ImportError = error;
            return;
        }

        _pendingContent  = content;
        _deleteStored    = false;
        ImportFileName   = Path.GetFileName(dlg.FileName);
        ImportSourceText = _storedFile is null
            ? "新匯入，按「確定並計算」後保存"
            : "新匯入，按「確定並計算」後覆蓋已保存的檔案";
    }

    /// <summary>刪除打卡資料：本次改為完全照班表計算；已保存的檔案在按「確定並計算」時才真的刪掉，按取消則保留</summary>
    [RelayCommand]
    private void DeleteAttendance()
    {
        ResetAttendance();
        ImportError     = string.Empty;
        _pendingContent = null;
        _deleteStored   = _storedFile is not null;
        PendingDeleteNote = _deleteStored
            ? $"已保存的「{_storedFile!.OriginalFileName}」將在按「確定並計算」後刪除；按取消則保留"
            : string.Empty;
    }

    private void TryLoadStored()
    {
        _storedFile = AttendanceFileStore.Get(_shopId, _schedule.Year, _schedule.Month);
        if (_storedFile is null) return;

        byte[] content;
        try { content = File.ReadAllBytes(_storedFile.FilePath); }
        catch (Exception ex)
        {
            ImportError = $"已保存的打卡資料無法讀取：{ex.Message}";
            return;
        }

        if (!LoadAttendance(content, out var error))
        {
            ImportError = $"已保存的打卡資料無法使用：{error}";
            return;
        }
        ImportFileName   = _storedFile.OriginalFileName;
        ImportSourceText = $"{_storedFile.ImportedAt:yyyy/MM/dd HH:mm} 匯入，本次沿用";
    }

    /// <summary>解析打卡檔並對應員工；失敗時不動目前已載入的資料</summary>
    private bool LoadAttendance(byte[] content, out string error)
    {
        error = string.Empty;
        List<ClockPerson> people;
        try
        {
            people = AttendanceImportService.Parse(content);
        }
        catch (Exception ex)
        {
            error = $"無法讀取檔案：{ex.Message}";
            return false;
        }

        // 只取班表當月的打卡
        foreach (var p in people)
            p.Punches.RemoveAll(x => x.Time.Year != _schedule.Year || x.Time.Month != _schedule.Month);
        var inMonth = people.Where(p => p.Punches.Count > 0).ToList();

        if (inMonth.Count == 0)
        {
            error = $"檔案中沒有 {_schedule.Year} 年 {_schedule.Month} 月的打卡紀錄";
            return false;
        }

        ResetAttendance();

        var byClockName = _activeEmployees
            .GroupBy(e => e.EffectiveClockName)
            .ToDictionary(g => g.Key, g => g.First());
        // 未設定薪資的員工也照常對應：是否缺薪資設定統一在按「確定並計算」時檢查（不匯入打卡也要檢查）
        var mappable = _activeEmployees.OrderBy(e => e.Name).ToList();

        foreach (var person in inMonth)
        {
            if (byClockName.TryGetValue(person.Name, out var emp))
            {
                if (!_matchedPunches.TryGetValue(emp.Id, out var list))
                    _matchedPunches[emp.Id] = list = new();
                list.AddRange(person.Punches);
            }
            else
            {
                UnmatchedNames.Add(new ClockNameMappingItem
                {
                    ClockName = person.Name,
                    DayCount  = person.Punches.Select(x => DateOnly.FromDateTime(x.Time)).Distinct().Count(),
                    Options   = mappable,
                    Punches   = person.Punches,
                });
            }
        }

        ImportSummary = $"已對應 {_matchedPunches.Count} 位員工"
                      + (UnmatchedNames.Count > 0 ? $"，{UnmatchedNames.Count} 個名字待指定" : string.Empty);
        ImportError   = string.Empty;
        HasAttendance = true;
        return true;
    }

    private void ResetAttendance()
    {
        _matchedPunches = new();
        UnmatchedNames.Clear();
        HasAttendance     = false;
        ImportFileName    = string.Empty;
        ImportSourceText  = string.Empty;
        ImportSummary     = string.Empty;
        PendingDeleteNote = string.Empty;
    }

    /// <summary>按「確定並計算」時才寫入：新匯入的覆蓋保存、按過刪除的移除</summary>
    public bool PersistAttendanceFile()
    {
        try
        {
            if (_pendingContent is not null)
                AttendanceFileStore.Save(_shopId, _schedule.Year, _schedule.Month,
                    _pendingContent, ImportFileName, DateTime.Now);
            else if (_deleteStored)
                AttendanceFileStore.Delete(_shopId, _schedule.Year, _schedule.Month);
            return true;
        }
        catch (Exception ex)
        {
            ValidationMessage = $"打卡資料存檔失敗：{ex.Message}";
            return false;
        }
    }

    // ── 按「確定並計算」時的薪資設定檢查 ─────────────────────
    private const string DefaultConfirmText = "確定並計算";

    [ObservableProperty] private string _validationMessage = string.Empty;
    [ObservableProperty] private string _confirmText = DefaultConfirmText;

    // 已提醒過、業主選擇略過的「未設定薪資」名單；名單變了要重新提醒
    private string? _acknowledgedNoSalaryKey;

    /// <summary>
    /// 本次會進入計算的員工（當月有排班，或匯入的打卡對應到的人）都要有薪資方案；
    /// 有勾選任何假日（含國定假日）時，時薪制員工還要有假日薪資方案。
    /// 缺假日薪資一律擋下；缺薪資方案提醒一次，再按一次則略過這些人照常計算。
    /// </summary>
    public bool Validate()
    {
        var attendanceIds = new HashSet<int>(_matchedPunches.Keys);
        foreach (var m in UnmatchedNames)
            if (m.SelectedEmployee is not null) attendanceIds.Add(m.SelectedEmployee.Id);

        var participants = _activeEmployees
            .Where(e => _employeeDates.ContainsKey(e.Id) || (HasAttendance && attendanceIds.Contains(e.Id)))
            .OrderBy(e => e.Name)
            .ToList();

        bool anyHoliday = IncludeNationalHolidays || WeekdayItems.Any(w => w.IsChecked && !w.IsShopClosed);
        var noHolidaySalary = anyHoliday
            ? participants.Where(e => e.DefaultSalary?.Type == SalaryType.Hourly && e.HolidaySalaryId is null).ToList()
            : new List<Employee>();

        if (noHolidaySalary.Count > 0)
        {
            _acknowledgedNoSalaryKey = null;
            ConfirmText       = DefaultConfirmText;
            ValidationMessage = "已勾選假日，但以下時薪制員工尚未設定假日薪資，請先至員工管理設定後再計算："
                              + string.Join("、", noHolidaySalary.Select(e => e.Name));
            return false;
        }

        var noSalary = participants.Where(e => e.DefaultSalary is null).ToList();
        if (noSalary.Count > 0)
        {
            var key = string.Join(",", noSalary.Select(e => e.Id));
            if (_acknowledgedNoSalaryKey == key) return true;

            _acknowledgedNoSalaryKey = key;
            ConfirmText       = "略過並計算";
            ValidationMessage = "以下員工尚未設定薪資方案，不會列入本次薪資計算："
                              + string.Join("、", noSalary.Select(e => e.Name))
                              + "。請先至員工管理設定，或按「略過並計算」繼續。";
            return false;
        }

        ValidationMessage = string.Empty;
        ConfirmText       = DefaultConfirmText;
        return true;
    }

    private Dictionary<int, Dictionary<DateOnly, DayPunch>>? BuildAttendance()
    {
        if (!HasAttendance) return null;

        var all = _matchedPunches.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        foreach (var m in UnmatchedNames.Where(m => m.SelectedEmployee is not null))
        {
            if (!all.TryGetValue(m.SelectedEmployee!.Id, out var list))
                all[m.SelectedEmployee.Id] = list = new();
            list.AddRange(m.Punches);
        }
        return all.ToDictionary(kv => kv.Key, kv => AttendanceImportService.BuildDays(kv.Value));
    }

    public SalaryCalculationConfig BuildConfig() => new()
    {
        LateGraceMinutes       = Math.Max(0, LateGraceMinutes),
        EarlyLeaveGraceMinutes = Math.Max(0, EarlyLeaveGraceMinutes),
        Attendance             = BuildAttendance(),
        ClockNameMappings      = UnmatchedNames
            .Where(m => m.SelectedEmployee is not null)
            .GroupBy(m => m.SelectedEmployee!.Id)
            .ToDictionary(g => g.Key, g => g.First().ClockName),
        HolidayDaysOfWeek = WeekdayItems
            .Where(w => w.IsChecked && !w.IsShopClosed)
            .Select(w => w.Day)
            .ToHashSet(),
        IncludeNationalHolidays = IncludeNationalHolidays,
        DailyOverrides = Overrides
            .Where(o => o.SelectedEmployee is not null && o.SelectedDate.HasValue)
            .Select(o => new DailyOverride
            {
                EmployeeId = o.SelectedEmployee!.Id,
                Date       = o.SelectedDate!.Value,
                Amount     = o.Amount,
            })
            .ToList(),
    };
}

public partial class ClockNameMappingItem : ObservableObject
{
    public string ClockName { get; init; } = string.Empty;
    public int DayCount { get; init; }
    public List<Employee> Options { get; init; } = new();
    public List<ClockPunch> Punches { get; init; } = new();

    [ObservableProperty] private Employee? _selectedEmployee;

    [RelayCommand] private void Skip() => SelectedEmployee = null;
}

public partial class WeekdayCheckItem : ObservableObject
{
    public DayOfWeek Day { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsShopClosed { get; init; }
    [ObservableProperty] private bool _isChecked;
}

public partial class DailyOverrideItem : ObservableObject
{
    private readonly Dictionary<int, List<DateOnly>> _employeeDates;

    public List<Employee> AllEmployees { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableDates))]
    private Employee? _selectedEmployee;

    public List<DateOnly> AvailableDates =>
        SelectedEmployee is not null && _employeeDates.TryGetValue(SelectedEmployee.Id, out var dates)
            ? dates
            : new List<DateOnly>();

    [ObservableProperty] private DateOnly? _selectedDate;
    [ObservableProperty] private decimal _amount;

    public DailyOverrideItem(List<Employee> employees, Dictionary<int, List<DateOnly>> employeeDates)
    {
        AllEmployees    = employees;
        _employeeDates  = employeeDates;
    }
}
