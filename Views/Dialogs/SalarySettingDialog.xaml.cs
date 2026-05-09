using MaterialDesignThemes.Wpf;
using ShopManager.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public record SalaryDialogResult(SalarySetting? WeekdayPlan, SalarySetting? HolidayPlan);

public partial class SalarySettingDialog : UserControl, INotifyPropertyChanged
{
    private readonly List<SalarySetting> _allSalaries;
    private SalaryType _selectedType;
    private SalarySetting? _selectedWeekdayPlan;
    private SalarySetting? _selectedHolidayPlan;

    public List<SalaryType> SalaryTypes { get; } = [SalaryType.Hourly, SalaryType.Monthly];

    public SalaryType SelectedType
    {
        get => _selectedType;
        set
        {
            _selectedType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHourly));
            OnPropertyChanged(nameof(FilteredSalaries));
            OnPropertyChanged(nameof(HasNoSalaries));
            SelectedWeekdayPlan = FilteredSalaries.FirstOrDefault();
            SelectedHolidayPlan = FilteredSalaries.FirstOrDefault();
        }
    }

    public bool IsHourly => _selectedType == SalaryType.Hourly;

    public List<SalarySetting> FilteredSalaries =>
        _allSalaries.Where(s => s.Type == _selectedType).ToList();

    public bool HasNoSalaries => FilteredSalaries.Count == 0;

    public SalarySetting? SelectedWeekdayPlan
    {
        get => _selectedWeekdayPlan;
        set { _selectedWeekdayPlan = value; OnPropertyChanged(); }
    }

    public SalarySetting? SelectedHolidayPlan
    {
        get => _selectedHolidayPlan;
        set { _selectedHolidayPlan = value; OnPropertyChanged(); }
    }

    public SalarySettingDialog(List<SalarySetting> salaries, SalarySetting? currentWeekday, SalarySetting? currentHoliday)
    {
        _allSalaries      = salaries;
        _selectedType     = currentWeekday?.Type ?? SalaryType.Hourly;
        _selectedWeekdayPlan = currentWeekday ?? _allSalaries.FirstOrDefault(s => s.Type == _selectedType);
        _selectedHolidayPlan = currentHoliday ?? _selectedWeekdayPlan;
        InitializeComponent();
        DataContext = this;
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var holidayPlan = IsHourly ? SelectedHolidayPlan : null;
        DialogHost.CloseDialogCommand.Execute(
            new SalaryDialogResult(SelectedWeekdayPlan, holidayPlan), this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
