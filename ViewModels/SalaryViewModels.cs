using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using System.Collections.ObjectModel;
using System.Text;

namespace ShopManager.ViewModels;

/// <summary>可選班表月份</summary>
public class SalaryScheduleItem
{
    public MonthlySchedule Schedule { get; init; } = null!;
    public string Label => $"{Schedule.Year} 年 {Schedule.Month} 月";
}

/// <summary>額外薪資項目（單行 VM，獎金／扣款）</summary>
public partial class BonusLineItem : ObservableObject
{
    public static IReadOnlyList<BonusPresetOption> Presets { get; } =
    [
        new(BonusPresetType.Custom,            "自訂"),
        new(BonusPresetType.PerfectAttendance, "全勤獎金"),
        new(BonusPresetType.Performance,       "績效獎金"),
        new(BonusPresetType.Project,           "專案獎金"),
        new(BonusPresetType.Transportation,    "交通補貼"),
        new(BonusPresetType.Meal,              "餐飲補貼"),
        new(BonusPresetType.Holiday,           "節日獎金"),
        new(BonusPresetType.YearEnd,           "年終獎金"),
        new(BonusPresetType.Deduction,         "扣款"),
        new(BonusPresetType.AttendanceAdjust,  "出勤差異"),
    ];

    // 這兩種類型的名稱由使用者／系統自訂（出勤差異會寫入「08/20 遲到 40 分」這類說明）
    public bool UsesCustomLabel => SelectedPreset.Type is BonusPresetType.Custom or BonusPresetType.AttendanceAdjust;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalChanged))]
    [NotifyPropertyChangedFor(nameof(UsesCustomLabel))]
    private BonusPresetOption _selectedPreset = Presets[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalChanged))]
    private string _customLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalChanged))]
    private decimal _amount;

    [ObservableProperty] private bool _isNew;

    public bool TotalChanged => true;

    public string Label => UsesCustomLabel ? CustomLabel : SelectedPreset.Label;

    public Action? OnChanged { get; set; }
    public Action? OnConfirm { get; set; }

    [RelayCommand]
    private void Confirm()
    {
        IsNew = false;
        OnConfirm?.Invoke();
    }

    partial void OnSelectedPresetChanged(BonusPresetOption value)
    {
        if (!UsesCustomLabel)
            CustomLabel = value.Label;
        OnChanged?.Invoke();
    }
    partial void OnCustomLabelChanged(string value) => OnChanged?.Invoke();
    partial void OnAmountChanged(decimal value)     => OnChanged?.Invoke();

    public SalaryBonusItem ToModel() => new()
    {
        Label      = Label,
        Amount     = Amount,
        PresetType = SelectedPreset.Type,
    };
}

public record BonusPresetOption(BonusPresetType Type, string Label);

/// <summary>每日排班工時明細（顯示用）</summary>
public class SalaryDailyEntry
{
    public DateOnly Date    { get; init; }
    public double   Hours   { get; init; }
    public string   TypeTag { get; init; } = string.Empty;   // 平日 / 假日 / 替代
    public string DayLabel => Date.DayOfWeek switch
    {
        DayOfWeek.Monday    => "一",
        DayOfWeek.Tuesday   => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday  => "四",
        DayOfWeek.Friday    => "五",
        DayOfWeek.Saturday  => "六",
        DayOfWeek.Sunday    => "日",
        _ => ""
    };
    public string Label => $"{Date.Month:D2}/{Date.Day:D2}（{DayLabel}）";
    public decimal? OverrideAmount { get; init; }
}

/// <summary>員工薪資卡片 VM</summary>
public partial class EmployeeSalaryItem : ObservableObject
{
    public Employee Employee { get; init; } = null!;
    public SalaryType SalaryType { get; init; }

    // 工時（接受出勤差異時會加回，故可寫）
    public double WeekdayHours { get; set; }
    public double HolidayHours { get; set; }
    public double OT1Hours     { get; set; }
    public double OT2Hours     { get; set; }
    public double TotalHours   => WeekdayHours + HolidayHours;

    // 打卡對照（ClockedHours 為 null＝未匯入打卡資料）
    public double  ScheduledHours { get; init; }
    public double? ClockedHours   { get; init; }
    public bool    HasAttendance  => ClockedHours.HasValue;

    // 薪資明細
    public decimal WeekdayPay  { get; set; }
    public decimal HolidayPay  { get; set; }
    public decimal OT1Pay      { get; set; }
    public decimal OT2Pay      { get; set; }
    public decimal OverridePay { get; init; }
    public decimal BaseAmount  { get; set; }

    // 費率描述
    public decimal HourlyRate        { get; init; }
    public decimal HolidayHourlyRate { get; init; }
    public decimal MonthlyBase       { get; init; }

    public List<SalaryDailyEntry> DailyEntries { get; } = new();

    [ObservableProperty] private bool _isScheduleDetailOpen;
    [RelayCommand] private void ToggleScheduleDetail() => IsScheduleDetailOpen = !IsScheduleDetailOpen;

    public ObservableCollection<BonusLineItem> BonusItems { get; } = new();
    public decimal BonusTotal => BonusItems.Sum(b => b.Amount);
    public decimal GrandTotal => BaseAmount + BonusTotal;

    public bool IsUnderMinWage { get; set; }
    public bool IsHourly  => SalaryType == SalaryType.Hourly;
    public bool IsMonthly => SalaryType == SalaryType.Monthly;
    public bool HasOT      => OT1Hours > 0 || OT2Hours > 0;
    public bool HasHoliday => HolidayHours > 0;
    public bool HasOverride => OverridePay != 0;

    [ObservableProperty] private bool _isExpanded = true;

    public Action? OnGlobalChanged { get; set; }

    public void RefreshTotals()
    {
        OnPropertyChanged(nameof(BonusTotal));
        OnPropertyChanged(nameof(GrandTotal));
    }

    [RelayCommand]
    private void AddBonus()
    {
        var bonus = new BonusLineItem { IsNew = true };
        bonus.OnChanged = () => RefreshTotals();
        bonus.OnConfirm = () => OnGlobalChanged?.Invoke();
        BonusItems.Add(bonus);
        RefreshTotals();
    }

    [RelayCommand]
    private void RemoveBonus(BonusLineItem item)
    {
        BonusItems.Remove(item);
        RefreshTotals();
        OnGlobalChanged?.Invoke();
    }

    // ── 出勤差異 ─────────────────────────────────────────────
    public ObservableCollection<AttendanceIssueItem> AttendanceIssues { get; } = new();
    public bool   HasPendingIssues  => AttendanceIssues.Count > 0;
    public int    UndecidedCount    => AttendanceIssues.Count(i => !i.IsDecided);
    public string PendingIssueLabel => $"{AttendanceIssues.Count} 筆差異待處理";
    public string AcceptButtonText  => UndecidedCount > 0 ? $"尚有 {UndecidedCount} 筆未選擇" : "接受";

    /// <summary>接受後由頁面重算最低薪資檢查、人事費用合計並自動存檔</summary>
    public Action<EmployeeSalaryItem>? OnAttendanceAccepted { get; set; }

    public void AddIssues(IEnumerable<AttendanceIssue> issues)
    {
        foreach (var issue in issues)
            AttendanceIssues.Add(new AttendanceIssueItem(issue)
            {
                OnChanged = () =>
                {
                    OnPropertyChanged(nameof(UndecidedCount));
                    OnPropertyChanged(nameof(AcceptButtonText));
                    AcceptIssuesCommand.NotifyCanExecuteChanged();
                    OnGlobalChanged?.Invoke();
                },
            });
    }

    private bool CanAcceptIssues() => AttendanceIssues.Count > 0 && UndecidedCount == 0;

    [RelayCommand(CanExecute = nameof(CanAcceptIssues))]
    private void AcceptIssues()
    {
        var adjustPreset = BonusLineItem.Presets.First(p => p.Type == BonusPresetType.AttendanceAdjust);

        foreach (var item in AttendanceIssues)
        {
            var issue = item.Issue;
            if (issue.Decision == AttendanceDecision.Count && issue.IfCounted is { } d)
            {
                WeekdayHours += d.WeekdayHours;
                HolidayHours += d.HolidayHours;
                OT1Hours     += d.OT1Hours;
                OT2Hours     += d.OT2Hours;
                WeekdayPay   += Math.Round(d.WeekdayPay, 0);
                HolidayPay   += Math.Round(d.HolidayPay, 0);
                OT1Pay       += Math.Round(d.OT1Pay,     0);
                OT2Pay       += Math.Round(d.OT2Pay,     0);
            }
            else if (issue.Decision is AttendanceDecision.Deduct or AttendanceDecision.Add)
            {
                var bonus = new BonusLineItem
                {
                    SelectedPreset = adjustPreset,
                    CustomLabel    = $"{issue.Date:MM/dd} {issue.ShortLabel}",
                    Amount         = issue.Decision == AttendanceDecision.Deduct ? -issue.Amount : issue.Amount,
                };
                bonus.OnChanged = () => { RefreshTotals(); OnGlobalChanged?.Invoke(); };
                BonusItems.Add(bonus);
            }
        }

        WeekdayHours = Math.Round(WeekdayHours, 2);
        HolidayHours = Math.Round(HolidayHours, 2);
        OT1Hours     = Math.Round(OT1Hours,     2);
        OT2Hours     = Math.Round(OT2Hours,     2);
        BaseAmount   = WeekdayPay + HolidayPay + OT1Pay + OT2Pay + OverridePay;

        AttendanceIssues.Clear();
        OnAttendanceAccepted?.Invoke(this);
        OnPropertyChanged(string.Empty);   // 工時、薪資、合計、差異區塊一次全部刷新
        AcceptIssuesCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>差異處理區的一列：業主必須逐筆選擇處理方式</summary>
public partial class AttendanceIssueItem : ObservableObject
{
    public AttendanceIssue Issue { get; }
    public Action? OnChanged { get; set; }

    public AttendanceIssueItem(AttendanceIssue issue)
    {
        Issue       = issue;
        _decision   = issue.Decision;
        _amountText = issue.Amount > 0 ? issue.Amount.ToString("0.##") : string.Empty;
    }

    public string DateLabel => $"{Issue.Date:MM/dd}（{"日一二三四五六"[(int)Issue.Date.DayOfWeek]}）";
    public string Description => Issue.Description;
    public string TypeLabel => Issue.Type switch
    {
        AttendanceIssueType.LateOrEarly => "遲到／早退",
        AttendanceIssueType.NoPunch     => "有排班沒打卡",
        AttendanceIssueType.Unscheduled => "沒排班有打卡",
        _                               => "只打一半卡",
    };

    // 遲到早退已照班表計薪：只能忽略或扣／加錢；其他類型可選計入（有可計入的工時時）或不計
    public bool ShowIgnore  => Issue.Type == AttendanceIssueType.LateOrEarly;
    public bool ShowCount   => Issue.IfCounted is not null;
    public bool ShowExclude => Issue.Type != AttendanceIssueType.LateOrEarly;
    public string CountLabel => Issue.Type == AttendanceIssueType.Unscheduled ? "依打卡計入" : "照班表計入";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIgnore), nameof(IsCount), nameof(IsExclude),
                              nameof(IsDeduct), nameof(IsAdd), nameof(NeedsAmount), nameof(IsDecided))]
    private AttendanceDecision _decision;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDecided))]
    private string _amountText;

    public bool IsIgnore  { get => Decision == AttendanceDecision.Ignore;  set { if (value) Decision = AttendanceDecision.Ignore;  } }
    public bool IsCount   { get => Decision == AttendanceDecision.Count;   set { if (value) Decision = AttendanceDecision.Count;   } }
    public bool IsExclude { get => Decision == AttendanceDecision.Exclude; set { if (value) Decision = AttendanceDecision.Exclude; } }
    public bool IsDeduct  { get => Decision == AttendanceDecision.Deduct;  set { if (value) Decision = AttendanceDecision.Deduct;  } }
    public bool IsAdd     { get => Decision == AttendanceDecision.Add;     set { if (value) Decision = AttendanceDecision.Add;     } }

    public bool NeedsAmount => Decision is AttendanceDecision.Deduct or AttendanceDecision.Add;

    private decimal? ParsedAmount =>
        decimal.TryParse(AmountText, out var a) && a > 0 ? a : null;

    public bool IsDecided => Decision switch
    {
        AttendanceDecision.None => false,
        AttendanceDecision.Deduct or AttendanceDecision.Add => ParsedAmount.HasValue,
        _ => true,
    };

    partial void OnDecisionChanged(AttendanceDecision value)
    {
        Issue.Decision = value;
        OnChanged?.Invoke();
    }

    partial void OnAmountTextChanged(string value)
    {
        Issue.Amount = ParsedAmount ?? 0;
        OnChanged?.Invoke();
    }
}

// ── 發薪紀錄視窗資料 ─────────────────────────────────────────────────────

public class PayrollRecordWindowData
{
    public SalaryRecord Record { get; init; } = null!;
    public List<BankCode> BankCodes { get; init; } = new();
    public Func<int, bool, Task> UpdatePaymentStatus { get; init; } = null!;
    /// <summary>推送薪資單圖片：(userId, PNG bytes) → success</summary>
    public Func<string, byte[], Task<bool>> SendLineImage { get; init; } = null!;
    public string ShopName { get; init; } = string.Empty;
}

public partial class PayrollEntryItem : ObservableObject
{
    public int RecordId { get; init; }
    public Employee Employee { get; init; } = null!;
    public decimal GrandTotal { get; init; }
    public string BankSummary { get; init; } = string.Empty;
    public bool HasLineBinding { get; init; }
    // salary data needed for LINE slip
    public SalaryEmployeeRecord EmpRecord { get; init; } = null!;
    public int Year { get; init; }
    public int Month { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaidAtLabel))]
    [NotifyPropertyChangedFor(nameof(CanSendLine))]
    private bool _isPaid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaidAtLabel))]
    private DateTime? _paidAt;

    [ObservableProperty] private bool _isSending;

    public string PaidAtLabel =>
        IsPaid && PaidAt.HasValue ? PaidAt.Value.ToString("yyyy/MM/dd HH:mm") : string.Empty;

    public bool CanSendLine    => HasLineBinding && IsPaid;
    public bool HasBankAccount => !string.IsNullOrEmpty(Employee.BankCode)
                                  && !string.IsNullOrEmpty(Employee.BankAccount);

    // 轉帳 QR Popup 狀態與快取圖
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QrBitmap))]
    private bool _isQrPopupOpen;

    private System.Windows.Media.Imaging.BitmapImage? _cachedQr;
    public System.Windows.Media.Imaging.BitmapImage? QrBitmap
    {
        get
        {
            if (!HasBankAccount) return null;
            if (_cachedQr is not null) return _cachedQr;
            var payload = Services.TaiwanPayQrService.BuildPayload(
                Employee.BankCode!, Employee.BankAccount!,
                GrandTotal, Employee.BankAccountName);
            _cachedQr = Services.TaiwanPayQrService.BuildBitmap(payload, 8);
            return _cachedQr;
        }
    }

    [RelayCommand]
    private void ToggleQrPopup() => IsQrPopupOpen = !IsQrPopupOpen;

    public Func<bool, Task>? OnIsPaidToggled { get; set; }
    public Func<Task>? OnSendLine { get; set; }

    // Set initial values without triggering OnIsPaidToggled callback
    public void SetInitialStatus(bool isPaid, DateTime? paidAt)
    {
        _isPaid = isPaid;
        _paidAt = paidAt;
    }

    partial void OnIsPaidChanged(bool value) => _ = HandlePaidAsync(value);

    private async Task HandlePaidAsync(bool isPaid)
    {
        if (OnIsPaidToggled is not null)
            await OnIsPaidToggled(isPaid);
        PaidAt = isPaid ? DateTime.Now : null;
    }

    [RelayCommand]
    private async Task SendLineAsync()
    {
        if (OnSendLine is null || IsSending) return;
        IsSending = true;
        try { await OnSendLine(); }
        finally { IsSending = false; }
    }

}
