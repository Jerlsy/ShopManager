using MaterialDesignThemes.Wpf;
using ShopManager.ViewModels;
using System.ComponentModel;
using System.Windows.Controls;

namespace ShopManager.Views.Dialogs;

public record ShiftPreferenceDialogResult(List<int> AvailableDays, List<int> PreferredShiftIds);

public partial class ShiftPreferenceDialog : UserControl
{
    private readonly List<AvailableDayItem> _dayItems;
    private readonly List<PreferShiftItem> _shiftItems;
    private bool _updatingShifts;

    public ShiftPreferenceDialog(
        List<AvailableDayItem> dayItems,
        List<PreferShiftItem> shiftItems,
        List<int> shopClosedDays)
    {
        // Clone so Cancel doesn't mutate ViewModel state
        _dayItems = dayItems.Select(d => new AvailableDayItem
        {
            Day = d.Day,
            Label = d.Label,
            IsAvailable = d.IsAvailable,
            IsShopClosed = shopClosedDays.Contains((int)d.Day)
        }).ToList();

        _shiftItems = shiftItems.Select(s => new PreferShiftItem
        {
            ShiftId = s.ShiftId,
            Label = s.Label,
            IsSelected = s.IsSelected
        }).ToList();

        foreach (var item in _shiftItems)
            item.PropertyChanged += OnShiftItemChanged;

        InitializeComponent();
        DayItemsControl.ItemsSource = _dayItems;
        ShiftItemsControl.ItemsSource = _shiftItems;
    }

    private void OnShiftItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PreferShiftItem.IsSelected)) return;
        if (_updatingShifts) return;
        if (sender is not PreferShiftItem changed || !changed.IsSelected) return;

        _updatingShifts = true;
        try
        {
            if (changed.IsUnlimited)
            {
                foreach (var s in _shiftItems.Where(s => !s.IsUnlimited))
                    s.IsSelected = false;
            }
            else
            {
                var unlimited = _shiftItems.FirstOrDefault(s => s.IsUnlimited);
                if (unlimited != null) unlimited.IsSelected = false;
            }
        }
        finally { _updatingShifts = false; }
    }

    private void OkButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var availDays = _dayItems
            .Where(d => d.IsAvailable && !d.IsShopClosed)
            .Select(d => (int)d.Day)
            .ToList();

        var unlimited = _shiftItems.FirstOrDefault(s => s.IsUnlimited);
        var preferredIds = (unlimited?.IsSelected == true)
            ? new List<int>()
            : _shiftItems.Where(s => !s.IsUnlimited && s.IsSelected)
                         .Select(s => s.ShiftId!.Value)
                         .ToList();

        DialogHost.CloseDialogCommand.Execute(
            new ShiftPreferenceDialogResult(availDays, preferredIds), this);
    }
}
