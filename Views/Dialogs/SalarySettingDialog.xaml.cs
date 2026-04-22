using MaterialDesignThemes.Wpf;
using ShopManager.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public record SalaryDialogResult(SalarySetting? Plan);

public partial class SalarySettingDialog : UserControl, INotifyPropertyChanged
{
    private readonly List<SalarySetting> _allSalaries;
    private SalaryType _selectedType;
    private SalarySetting? _selectedPlan;

    public List<SalaryType> SalaryTypes { get; } = [SalaryType.Hourly, SalaryType.Monthly, SalaryType.Contract];

    public SalaryType SelectedType
    {
        get => _selectedType;
        set
        {
            _selectedType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilteredSalaries));
            OnPropertyChanged(nameof(HasNoSalaries));
            SelectedPlan = FilteredSalaries.FirstOrDefault();
        }
    }

    public List<SalarySetting> FilteredSalaries =>
        _allSalaries.Where(s => s.Type == _selectedType).ToList();

    public bool HasNoSalaries => FilteredSalaries.Count == 0;

    public SalarySetting? SelectedPlan
    {
        get => _selectedPlan;
        set { _selectedPlan = value; OnPropertyChanged(); }
    }

    public SalarySettingDialog(List<SalarySetting> salaries, SalarySetting? current)
    {
        _allSalaries = salaries;
        _selectedType = current?.Type ?? SalaryType.Hourly;
        _selectedPlan = current ?? _allSalaries.FirstOrDefault(s => s.Type == _selectedType);
        InitializeComponent();
        DataContext = this;
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
        => DialogHost.CloseDialogCommand.Execute(new SalaryDialogResult(SelectedPlan), this);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
