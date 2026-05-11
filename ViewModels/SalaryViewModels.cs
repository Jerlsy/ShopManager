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
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalChanged))]
    private BonusPresetOption _selectedPreset = Presets[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalChanged))]
    private string _customLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalChanged))]
    private decimal _amount;

    [ObservableProperty] private bool _isNew;

    public bool TotalChanged => true;

    public string Label => SelectedPreset.Type == BonusPresetType.Custom
        ? CustomLabel
        : SelectedPreset.Label;

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
        if (value.Type != BonusPresetType.Custom)
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

    // 工時
    public double WeekdayHours { get; init; }
    public double HolidayHours { get; init; }
    public double OT1Hours     { get; init; }
    public double OT2Hours     { get; init; }
    public double TotalHours   => WeekdayHours + HolidayHours;

    // 薪資明細
    public decimal WeekdayPay  { get; init; }
    public decimal HolidayPay  { get; init; }
    public decimal OT1Pay      { get; init; }
    public decimal OT2Pay      { get; init; }
    public decimal OverridePay { get; init; }
    public decimal BaseAmount  { get; init; }

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
}

// ── 發薪紀錄視窗資料 ─────────────────────────────────────────────────────

public class PayrollRecordWindowData
{
    public SalaryRecord Record { get; init; } = null!;
    public List<BankCode> BankCodes { get; init; } = new();
    public Func<int, bool, Task> UpdatePaymentStatus { get; init; } = null!;
    public Func<string, string, Task<bool>> SendLineMessage { get; init; } = null!;
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

    public bool CanSendLine => HasLineBinding && IsPaid;

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

    public static string BuildSalarySlip(SalaryEmployeeRecord r, int year, int month, DateTime? paidAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"【{year}年{month}月 薪資單】");
        sb.AppendLine();
        sb.AppendLine($"員工：{r.Employee.Name}");
        sb.AppendLine(r.SalaryType == SalaryType.Hourly ? "薪資制度：時薪制" : "薪資制度：月薪制");
        sb.AppendLine("─────────────────");

        if (r.SalaryType == SalaryType.Hourly)
        {
            sb.AppendLine($"平日工時：{r.WeekdayHours:N1} hr");
            if (r.HolidayHours > 0) sb.AppendLine($"假日工時：{r.HolidayHours:N1} hr");
            if (r.OT1Hours > 0)     sb.AppendLine($"加班一段：{r.OT1Hours:N1} hr");
            if (r.OT2Hours > 0)     sb.AppendLine($"加班二段：{r.OT2Hours:N1} hr");
            sb.AppendLine($"平日薪資：{r.WeekdayPay:N0} 元");
            if (r.HolidayPay > 0)   sb.AppendLine($"假日薪資：{r.HolidayPay:N0} 元");
            if (r.OT1Pay + r.OT2Pay > 0) sb.AppendLine($"加班費：{r.OT1Pay + r.OT2Pay:N0} 元");
        }
        else
        {
            sb.AppendLine($"底薪：{r.WeekdayPay:N0} 元");
            if (r.HolidayPay > 0)            sb.AppendLine($"假日薪資：{r.HolidayPay:N0} 元");
            if (r.OT1Pay + r.OT2Pay > 0)     sb.AppendLine($"加班費：{r.OT1Pay + r.OT2Pay:N0} 元");
        }
        if (r.OverridePay != 0) sb.AppendLine($"特殊薪資：{r.OverridePay:N0} 元");

        if (r.BonusItems.Count > 0)
        {
            sb.AppendLine("─────────────────");
            foreach (var b in r.BonusItems)
                sb.AppendLine($"{b.Label}：{(b.Amount >= 0 ? "+" : "")}{b.Amount:N0} 元");
        }

        var grand = r.BaseAmount + r.BonusItems.Sum(b => b.Amount);
        sb.AppendLine("─────────────────");
        sb.AppendLine($"應領薪資：{grand:N0} 元");

        if (paidAt.HasValue)
            sb.AppendLine($"支薪日期：{paidAt.Value:yyyy/MM/dd}");

        return sb.ToString().TrimEnd();
    }
}
