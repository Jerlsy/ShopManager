using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShopManager.Helpers;

/// <summary>
/// 金額輸入框只允許整數：擋掉打字與貼上的非數字字元（含小數點）。
/// 用法：helpers:IntegerInput.Mode="Positive"（只能正整數）或 "Signed"（可輸入負號，例如扣款）。
/// 倍率類欄位（加班倍率 1.34 等）本來就需要小數，不要套用。
/// </summary>
public static class IntegerInput
{
    public enum InputMode { None, Positive, Signed }

    public static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "Mode", typeof(InputMode), typeof(IntegerInput), new PropertyMetadata(InputMode.None, OnModeChanged));

    public static InputMode GetMode(DependencyObject d) => (InputMode)d.GetValue(ModeProperty);
    public static void SetMode(DependencyObject d, InputMode value) => d.SetValue(ModeProperty, value);

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        tb.PreviewTextInput -= OnPreviewTextInput;
        DataObject.RemovePastingHandler(tb, OnPasting);
        InputMethod.SetIsInputMethodEnabled(tb, true);

        if ((InputMode)e.NewValue == InputMode.None) return;

        tb.PreviewTextInput += OnPreviewTextInput;
        DataObject.AddPastingHandler(tb, OnPasting);
        InputMethod.SetIsInputMethodEnabled(tb, false);   // 關掉中文輸入法，避免組字繞過檢查
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !IsValid(proposed, GetMode(tb));
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var tb = (TextBox)sender;
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text) { e.CancelCommand(); return; }
        var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, text.Trim());
        if (!IsValid(proposed, GetMode(tb))) e.CancelCommand();
    }

    private static bool IsValid(string text, InputMode mode)
    {
        if (mode == InputMode.Signed && text.StartsWith('-')) text = text[1..];
        return text.All(char.IsAsciiDigit);
    }
}
