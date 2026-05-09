using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using System.Collections.ObjectModel;

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

    public bool TotalChanged => true;

    public string Label => SelectedPreset.Type == BonusPresetType.Custom
        ? CustomLabel
        : SelectedPreset.Label;

    public Action? OnChanged { get; set; }

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

    public void RefreshTotals()
    {
        OnPropertyChanged(nameof(BonusTotal));
        OnPropertyChanged(nameof(GrandTotal));
    }

    [RelayCommand]
    private void AddBonus()
    {
        var item = new BonusLineItem();
        item.OnChanged = () => RefreshTotals();
        BonusItems.Add(item);
        RefreshTotals();
    }

    [RelayCommand]
    private void RemoveBonus(BonusLineItem item)
    {
        BonusItems.Remove(item);
        RefreshTotals();
    }
}
