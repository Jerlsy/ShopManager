using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ShopManager.ViewModels;

namespace ShopManager.Helpers;

/// <summary>
/// 月視圖班別橫條內的頭像容器：水平排列頭像，
/// 若全部頭像在可用寬度內放不下，就「收合」——把頭像排版為 0 寬（隱藏、停用拖拉），
/// 並透過 DataContext(ShiftBlock).IsOverflowing 通知 View 改顯示單一計數圈。
/// 可用寬度由月格欄寬決定、與收合與否無關，故不會在收合/展開間來回震盪。
/// </summary>
public class CollapsingAvatarPanel : Panel
{
    private bool _overflow;   // 量測當下計算，供同一輪 Arrange 即時使用（避免延後屬性造成首幀閃爍）

    protected override Size MeasureOverride(Size availableSize)
    {
        double totalW = 0, maxH = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            totalW += child.DesiredSize.Width;
            maxH = System.Math.Max(maxH, child.DesiredSize.Height);
        }

        _overflow = InternalChildren.Count > 1
            && !double.IsInfinity(availableSize.Width)
            && totalW > availableSize.Width;

        // 量測期間不直接改 UI（避免重入），改用 Dispatcher 延後回報，且僅在值改變時更新
        if (DataContext is ShiftBlock sb && sb.IsOverflowing != _overflow)
            Dispatcher.BeginInvoke(new System.Action(() => sb.IsOverflowing = _overflow),
                DispatcherPriority.Render);

        return _overflow ? new Size(0, maxH)
                         : new Size(System.Math.Min(totalW, availableSize.Width), maxH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_overflow)
        {
            // 收合：頭像排為 0 寬（不可見、不可拖拉）
            foreach (UIElement child in InternalChildren)
                child.Arrange(new Rect(0, 0, 0, 0));
        }
        else
        {
            double x = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width;
            }
        }
        return finalSize;
    }
}
