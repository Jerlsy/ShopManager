using Microsoft.Win32;
using ShopManager.Models;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShopManager.Views.Schedule;

public partial class ExportScheduleWindow : Window
{
    private readonly ExportScheduleData _data;
    private RenderTargetBitmap? _bitmap;

    public ExportScheduleWindow(ExportScheduleData data)
    {
        InitializeComponent();
        _data = data;
        Title = $"{data.Year} 年 {data.Month:D2} 月  班表匯出";
        Loaded += (_, _) =>
        {
            _bitmap = RenderSchedule(data);
            PreviewImage.Source = _bitmap;
        };
    }

    private void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        if (_bitmap is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 圖片|*.png",
            FileName = $"班表_{_data.Year}{_data.Month:D2}",
            DefaultExt = "png"
        };
        if (dlg.ShowDialog() != true) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_bitmap));
        using var fs = File.OpenWrite(dlg.FileName);
        encoder.Save(fs);
    }

    private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
    {
        if (_bitmap is null) return;
        Clipboard.SetImage(_bitmap);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ─────────────────────────────────────────────────────────────────────────
    // 班表圖片渲染
    // 所有座標與字體直接以 1.5× 繪製，DrawingVisual 不做事後縮放，
    // 保證文字由 WPF 字型引擎在目標解析度下原生渲染，無鋸齒。
    // ─────────────────────────────────────────────────────────────────────────
    internal static RenderTargetBitmap RenderSchedule(ExportScheduleData data)
    {
        const double dpi   = 96;
        const double scale = 1.5;

        // ── 版面常數（邏輯尺寸 × scale，直接繪製到目標像素）──────────────
        double S(double v) => v * scale;

        double nameW        = S(90);   // 員工姓名欄寬
        double cellW        = S(44);   // 日期格寬
        double titleH       = S(44);   // 標題列高
        double colH         = S(46);   // 欄標題高
        double rowH         = S(34);   // 資料列高
        double legTopGap    = S(20);   // 表格底部到圖例框的白色間距
        double legPadV      = S(12);   // 圖例框上下 padding
        double legTitleRowH = S(22);   // 圖例框內「班別說明」標題列高
        double legTitleGap  = S(6);    // 標題列下到第一條圖例的間距
        double legItemH     = S(28);   // 每條圖例高
        double legItemW     = S(180);  // 每條圖例欄寬（橫排用）
        double legSwatchSz  = S(14);   // 色塊大小

        // 字體 typeface
        var fontFamily  = new FontFamily("Microsoft JhengHei UI, Microsoft JhengHei, sans-serif");
        var normalFace  = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal,   FontStretches.Normal);
        var boldFace    = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold,     FontStretches.Normal);
        var semiBoldFace= new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        // 班別顏色快取
        var colorMap = data.ShiftLegend.ToDictionary(l => l.Id, l => ParseHex(l.ColorHex));

        double tableW = nameW + data.DaysInMonth * cellW;
        double tableH = colH + data.Rows.Count * rowH;

        // 圖例區：計算橫排後需幾列
        int itemsPerRow  = data.ShiftLegend.Count == 0 ? 1
            : Math.Max(1, (int)((tableW - S(24)) / legItemW));
        int itemRowCount = data.ShiftLegend.Count == 0 ? 0
            : (data.ShiftLegend.Count + itemsPerRow - 1) / itemsPerRow;
        double legBoxH = data.ShiftLegend.Count > 0
            ? legPadV + legTitleRowH + legTitleGap + itemRowCount * legItemH + legPadV
            : 0;
        double legAreaH = data.ShiftLegend.Count > 0 ? legTopGap + legBoxH : 0;

        double totalW = tableW;
        double totalH = titleH + tableH + legAreaH + 1;

        var pen05    = FreezePen(Color.FromRgb(0xCC, 0xCC, 0xCC), 0.5);
        var pen10    = FreezePen(Color.FromRgb(0x99, 0xAA, 0xBB), 1.0);
        var outerPen = FreezePen(Color.FromRgb(0x88, 0x99, 0xAA), 1.5);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));

            // ── 標題列 ──────────────────────────────────────────────────────
            dc.DrawRectangle(FreezeColor(Color.FromRgb(0x2A, 0x5C, 0x8A)), null,
                new Rect(0, 0, totalW, titleH));
            var titleT = Fmt($"{data.ShopName}　{data.Year} 年 {data.Month} 月　班表",
                boldFace, S(15), Brushes.White);
            dc.DrawText(titleT, new Point(S(14), (titleH - titleT.Height) / 2));

            // ── 欄標題列 ─────────────────────────────────────────────────────
            double colY        = titleH;
            var    colHeaderBg = FreezeColor(Color.FromRgb(0xE3, 0xEE, 0xF7));
            dc.DrawRectangle(colHeaderBg, null, new Rect(0, colY, totalW, colH));

            dc.DrawRectangle(null, pen10, new Rect(0, colY, nameW, colH));
            var nameHdrT = Fmt("員工", semiBoldFace, S(12), FreezeColor(Color.FromRgb(0x44, 0x66, 0x88)));
            dc.DrawText(nameHdrT, new Point((nameW - nameHdrT.Width) / 2,
                colY + (colH - nameHdrT.Height) / 2));

            for (int i = 0; i < data.Columns.Count; i++)
            {
                var col = data.Columns[i];
                double x = nameW + i * cellW;

                Brush colCellBg; Brush dowFg;
                if (col.IsClosed)
                { colCellBg = FreezeColor(Color.FromRgb(0xD5, 0xD5, 0xD5)); dowFg = Brushes.Gray; }
                else if (col.DayOfWeekLabel == "日")
                { colCellBg = FreezeColor(Color.FromRgb(0xFF, 0xE3, 0xE3)); dowFg = Brushes.Crimson; }
                else if (col.DayOfWeekLabel == "六")
                { colCellBg = FreezeColor(Color.FromRgb(0xE3, 0xEE, 0xFF)); dowFg = Brushes.RoyalBlue; }
                else
                { colCellBg = colHeaderBg; dowFg = FreezeColor(Color.FromRgb(0x44, 0x55, 0x66)); }

                dc.DrawRectangle(colCellBg, null, new Rect(x, colY, cellW, colH));
                dc.DrawRectangle(null, pen05, new Rect(x, colY, cellW, colH));

                var dayT = Fmt(col.Day.ToString(), boldFace, S(14), Brushes.Black);
                dc.DrawText(dayT, new Point(x + (cellW - dayT.Width) / 2, colY + S(4)));

                var dowT = Fmt(col.DayOfWeekLabel, normalFace, S(11), dowFg);
                dc.DrawText(dowT, new Point(x + (cellW - dowT.Width) / 2,
                    colY + colH - dowT.Height - S(5)));
            }

            // ── 資料列 ───────────────────────────────────────────────────────
            for (int r = 0; r < data.Rows.Count; r++)
            {
                var row  = data.Rows[r];
                double y = titleH + colH + r * rowH;

                dc.DrawRectangle(r % 2 == 0 ? Brushes.White
                    : (Brush)FreezeColor(Color.FromRgb(0xF6, 0xFA, 0xFD)),
                    null, new Rect(0, y, totalW, rowH));

                dc.DrawRectangle(null, pen10, new Rect(0, y, nameW, rowH));
                var nameT = Fmt(row.Name, semiBoldFace, S(13), Brushes.Black);
                dc.DrawText(nameT, new Point(S(8), y + (rowH - nameT.Height) / 2));

                for (int c = 0; c < row.ShiftIds.Count && c < data.Columns.Count; c++)
                {
                    var col     = data.Columns[c];
                    var shiftId = row.ShiftIds[c];
                    double x    = nameW + c * cellW;

                    if (col.IsClosed)
                    {
                        dc.DrawRectangle(FreezeColor(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                            null, new Rect(x, y, cellW, rowH));
                        var ct = Fmt("休", normalFace, S(11), Brushes.Gray);
                        dc.DrawText(ct, new Point(x + (cellW - ct.Width) / 2,
                            y + (rowH - ct.Height) / 2));
                    }
                    else if (shiftId.HasValue && colorMap.TryGetValue(shiftId.Value, out var sc))
                    {
                        var fill = new SolidColorBrush(Color.FromArgb(0xCC, sc.R, sc.G, sc.B));
                        fill.Freeze();
                        dc.DrawRectangle(fill, null, new Rect(x, y, cellW, rowH));
                    }

                    dc.DrawRectangle(null, pen05, new Rect(x, y, cellW, rowH));
                }
            }

            dc.DrawRectangle(null, outerPen, new Rect(0, titleH, totalW, tableH));

            // ── 圖例區 ───────────────────────────────────────────────────────
            if (data.ShiftLegend.Count > 0)
            {
                // legTopGap 是白色留白，不需繪製背景（已是白底）
                double legBoxY = titleH + tableH + legTopGap;

                dc.DrawRectangle(FreezeColor(Color.FromRgb(0xF1, 0xF6, 0xFB)), null,
                    new Rect(0, legBoxY, totalW, legBoxH));
                dc.DrawRectangle(null, FreezePen(Color.FromRgb(0xBB, 0xCC, 0xDD), 1.0),
                    new Rect(0, legBoxY, totalW, legBoxH));

                // 「班別說明」標題
                var legHdrT = Fmt("班別說明", semiBoldFace, S(12),
                    FreezeColor(Color.FromRgb(0x33, 0x55, 0x77)));
                dc.DrawText(legHdrT, new Point(S(12),
                    legBoxY + legPadV + (legTitleRowH - legHdrT.Height) / 2));

                // 圖例項目
                double itemsY = legBoxY + legPadV + legTitleRowH + legTitleGap;
                double legX   = S(12);
                int    col_i  = 0;

                foreach (var leg in data.ShiftLegend)
                {
                    if (col_i >= itemsPerRow) { col_i = 0; legX = S(12); itemsY += legItemH; }

                    var swatchC = ParseHex(leg.ColorHex);
                    var swatchB = new SolidColorBrush(swatchC); swatchB.Freeze();
                    dc.DrawRectangle(swatchB, FreezePen(Color.FromRgb(0x88, 0x88, 0x88), 0.5),
                        new Rect(legX, itemsY + (legItemH - legSwatchSz) / 2,
                            legSwatchSz, legSwatchSz));

                    var legT = Fmt($"{leg.Alias}  {leg.TimeRange}", normalFace, S(12), Brushes.Black);
                    dc.DrawText(legT, new Point(legX + legSwatchSz + S(6),
                        itemsY + (legItemH - legT.Height) / 2));

                    legX += legItemW;
                    col_i++;
                }
            }
        }

        // ScaleTransform 移除：DrawingVisual 已在目標解析度直接繪製
        var rtb = new RenderTargetBitmap((int)totalW, (int)totalH, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }

    // ── 小工具 ────────────────────────────────────────────────────────────────
    private static Color ParseHex(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.SteelBlue; }
    }

    private static SolidColorBrush FreezeColor(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FreezePen(Color c, double thickness)
    {
        var p = new Pen(new SolidColorBrush(c), thickness);
        p.Freeze();
        return p;
    }

    private static FormattedText Fmt(string text, Typeface typeface, double size, Brush fg) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, fg, 1.0);
}
