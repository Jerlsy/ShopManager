using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopManager.Models;
using ShopManager.Services;

namespace ShopManager.ViewModels;

public partial class SalarySettingViewModel(
    SalarySettingService service,
    ScheduleConflictService conflictService,
    IAppSnackbarService snackbarService,
    IAppDialogService dialogService) : ObservableObject
{
    [ObservableProperty] private bool _showLaborLaw;
    [ObservableProperty] private LaborLawSetting _laborLaw = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    private List<SalarySetting> _salaries = new();

    [ObservableProperty] private SalarySetting? _selectedSalary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    private bool _isEditing;

    public bool ShowEmptyHint => !IsEditing && Salaries.Count == 0;

    [ObservableProperty] private string _editAlias = string.Empty;
    [ObservableProperty] private string _editDescription = string.Empty;
    [ObservableProperty] private SalaryType _editType = SalaryType.Hourly;
    [ObservableProperty] private decimal? _editHourlyRate;
    [ObservableProperty] private decimal? _editMonthlyBase;
    [ObservableProperty] private decimal? _editOT1Rate;
    [ObservableProperty] private decimal? _editOT2Rate;
    [ObservableProperty] private decimal? _editHolidayRate;
    [ObservableProperty] private double? _editDailyMaxHours;
    [ObservableProperty] private double? _editWeeklyMaxHours;

    public static List<SalaryType> SalaryTypes { get; } =
        Enum.GetValues<SalaryType>().ToList();

    public async Task LoadAsync()
    {
        LaborLaw = await service.GetLaborLawAsync() ?? new LaborLawSetting();
        Salaries = await service.GetAllAsync();
    }

    [RelayCommand] public void OpenLaborLaw() => ShowLaborLaw = true;
    [RelayCommand] public void CloseLaborLaw() => ShowLaborLaw = false;

    [RelayCommand]
    public async Task SaveLaborLawAsync()
    {
        await service.SaveLaborLawAsync(LaborLaw);
        ShowLaborLaw = false;
        snackbarService.ShowSuccess("勞基法設定已儲存");

        var conflictCount = await conflictService.RecheckAllForShopAsync();
        if (conflictCount > 0)
            snackbarService.ShowWarning($"儲存後發現 {conflictCount} 條排班衝突，請至排班頁面調整");
    }

    partial void OnEditTypeChanged(SalaryType value)
    {
        EditOT1Rate = value == SalaryType.Hourly ? LaborLaw.HourlyOT1Rate : LaborLaw.MonthlyOT1Rate;
        EditOT2Rate = value == SalaryType.Hourly ? LaborLaw.HourlyOT2Rate : LaborLaw.MonthlyOT2Rate;
        EditHolidayRate = LaborLaw.HolidayOTRate;
        EditDailyMaxHours = LaborLaw.DailyMaxHours;
        EditWeeklyMaxHours = LaborLaw.WeeklyMaxHours;
    }

    [RelayCommand]
    public void StartNew()
    {
        SelectedSalary = null;
        EditAlias = string.Empty;
        EditDescription = string.Empty;
        EditType = SalaryType.Hourly;
        EditHourlyRate = null;
        EditMonthlyBase = null;
        EditOT1Rate = null;
        EditOT2Rate = null;
        EditHolidayRate = null;
        EditDailyMaxHours = null;
        EditWeeklyMaxHours = null;
        IsEditing = true;
    }

    [RelayCommand]
    public void StartEdit(SalarySetting s)
    {
        SelectedSalary = s;
        EditAlias = s.Alias;
        EditDescription = s.Description;
        EditType = s.Type;
        EditHourlyRate = s.HourlyRate;
        EditMonthlyBase = s.MonthlyBase;
        EditOT1Rate = s.OT1Rate;
        EditOT2Rate = s.OT2Rate;
        EditHolidayRate = s.HolidayRate;
        EditDailyMaxHours = s.DailyMaxHours;
        EditWeeklyMaxHours = s.WeeklyMaxHours;
        IsEditing = true;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var salary = SelectedSalary ?? new SalarySetting();
        salary.Alias = EditAlias;
        salary.Description = EditDescription;
        salary.Type = EditType;
        salary.HourlyRate = EditHourlyRate;
        salary.MonthlyBase = EditMonthlyBase;
        salary.OT1Rate = EditOT1Rate;
        salary.OT2Rate = EditOT2Rate;
        salary.HolidayRate = EditHolidayRate;
        salary.DailyMaxHours = EditDailyMaxHours;
        salary.WeeklyMaxHours = EditWeeklyMaxHours;

        if (SelectedSalary is null) await service.AddAsync(salary);
        else await service.UpdateAsync(salary);

        IsEditing = false;
        await LoadAsync();
        snackbarService.ShowSuccess("薪資設定已儲存");
    }

    [RelayCommand]
    public async Task DeleteAsync(SalarySetting s)
    {
        var confirmed = await dialogService.ShowConfirmAsync(
            "確認刪除",
            $"確定要刪除薪資方案「{s.Alias}」嗎？此操作無法復原。",
            "刪除", "取消");

        if (!confirmed) return;

        await service.DeleteAsync(s.Id);
        await LoadAsync();
    }

    [RelayCommand] public void Cancel() => IsEditing = false;
}
