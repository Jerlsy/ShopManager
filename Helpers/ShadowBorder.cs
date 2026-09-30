using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ShopManager.Helpers;

/// <summary>
/// 帶陰影的 Border：陰影改由內部一塊「只畫背景與框線、沒有子元素」的底板承擔，內容疊在底板上。
/// 直接在容器設 Effect 時，容器內任何重繪（滑鼠移過、輸入游標閃爍、捲動）都要連同整塊內容重算模糊；
/// 分開後內容變動不會牽動陰影，外觀輪廓不變。
/// </summary>
public class ShadowBorder : Border
{
    public static readonly DependencyProperty ShadowProperty = DependencyProperty.Register(
        nameof(Shadow), typeof(Effect), typeof(ShadowBorder),
        new PropertyMetadata(null, (d, e) => ((ShadowBorder)d)._plate.Effect = (Effect?)e.NewValue));

    public Effect? Shadow
    {
        get => (Effect?)GetValue(ShadowProperty);
        set => SetValue(ShadowProperty, value);
    }

    private readonly Border _plate = new();

    public ShadowBorder()
    {
        Mirror(BackgroundProperty);
        Mirror(BorderBrushProperty);
        Mirror(BorderThicknessProperty);
        Mirror(CornerRadiusProperty);
        Mirror(SnapsToDevicePixelsProperty);
        AddVisualChild(_plate);
    }

    private void Mirror(DependencyProperty dp) =>
        _plate.SetBinding(dp, new Binding { Source = this, Path = new PropertyPath(dp), Mode = BindingMode.OneWay });

    protected override int VisualChildrenCount => Child is null ? 1 : 2;

    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _plate,
        1 when Child is not null => Child,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    protected override Size MeasureOverride(Size constraint)
    {
        _plate.Measure(constraint);
        return base.MeasureOverride(constraint);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _plate.Arrange(new Rect(finalSize));
        return base.ArrangeOverride(finalSize);
    }

    // 背景與框線已由底板繪製；這裡再畫一次會讓半透明框線疊成兩倍深
    protected override void OnRender(DrawingContext dc) { }
}
