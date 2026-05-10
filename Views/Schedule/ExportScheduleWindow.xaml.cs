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
    // ─────────────────────────────────────────────────────────────────────────
    private static RenderTargetBitmap RenderSchedule(ExportScheduleData data)
    {
        const double dpi    = 96;
        const double scale  = 1.5;
        const double nameW  = 90;   // 員工姓名欄寬
        const double cellW  = 44;   // 日期格寬
        const double titleH = 44;   // 標題列高
        const double colH   = 46;   // 欄標題高
        const double rowH   = 34;   // 資料列高
        const double legendH = 28;  // 每條圖例高
        const double legendPad = 14; // 圖例區上下留白
        const double legendSwatchSize = 14; // 色塊大小

        var fontFamily  = new FontFamily("Microsoft JhengHei UI, Microsoft JhengHei, sans-serif");
        var normalFace  = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal,   FontStretches.Normal);
        var boldFace    = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold,     FontStretches.Normal);
        var semiBoldFace= new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        // 班別顏色快取（附透明度以配合淡色格）
        var colorMap = data.ShiftLegend.ToDictionary(
            l => l.Id,
            l => ParseHex(l.ColorHex));

        double tableW = nameW + data.DaysInMonth * cellW;
        double tableH = colH + data.Rows.Count * rowH;

        // 圖例區高度（至少保留留白，有圖例才算）
        double legendAreaH = data.ShiftLegend.Count > 0
            ? legendPad * 2 + data.ShiftLegend.Count * legendH
            : 0;

        double totalW = tableW;
        double totalH = titleH + tableH + legendAreaH + 1;

        var pen05 = FreezePen(Color.FromRgb(0xCC, 0xCC, 0xCC), 0.5);
        var pen10 = FreezePen(Color.FromRgb(0x99, 0xAA, 0xBB), 1.0);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 白底
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));

            // ── 標題列 ──────────────────────────────────────────────────────
            var titleBg = FreezeColor(Color.FromRgb(0x2A, 0x5C, 0x8A));
            dc.DrawRectangle(titleBg, null, new Rect(0, 0, totalW, titleH));
            var titleT = Fmt($"{data.ShopName}　{data.Year} 年 {data.Month} 月　班表", boldFace, 15, Brushes.White);
            dc.DrawText(titleT, new Point(14, (titleH - titleT.Height) / 2));

            // ── 欄標題列 ─────────────────────────────────────────────────────
            double colY = titleH;
            var colHeaderBg = FreezeColor(Color.FromRgb(0xE3, 0xEE, 0xF7));
            dc.DrawRectangle(colHeaderBg, null, new Rect(0, colY, totalW, colH));

            // 員工名稱欄標題
            dc.DrawRectangle(null, pen10, new Rect(0, colY, nameW, colH));
            var nameHdrT = Fmt("員工", semiBoldFace, 12, FreezeColor(Color.FromRgb(0x44, 0x66, 0x88)));
            dc.DrawText(nameHdrT, new Point((nameW - nameHdrT.Width) / 2, colY + (colH - nameHdrT.Height) / 2));

            for (int i = 0; i < data.Columns.Count; i++)
            {
                var col = data.Columns[i];
                var x   = nameW + i * cellW;

                Brush colCellBg;
                Brush dowFg;
                if (col.IsClosed)
                {
                    colCellBg = FreezeColor(Color.FromRgb(0xD5, 0xD5, 0xD5));
                    dowFg     = Brushes.Gray;
                }
                else if (col.DayOfWeekLabel == "日")
                {
                    colCellBg = FreezeColor(Color.FromRgb(0xFF, 0xE3, 0xE3));
                    dowFg     = Brushes.Crimson;
                }
                else if (col.DayOfWeekLabel == "六")
                {
                    colCellBg = FreezeColor(Color.FromRgb(0xE3, 0xEE, 0xFF));
                    dowFg     = Brushes.RoyalBlue;
                }
                else
                {
                    colCellBg = colHeaderBg;
                    dowFg     = FreezeColor(Color.FromRgb(0x44, 0x55, 0x66));
                }

                dc.DrawRectangle(colCellBg, null, new Rect(x, colY, cellW, colH));
                dc.DrawRectangle(null, pen05, new Rect(x, colY, cellW, colH));

                var dayT = Fmt(col.Day.ToString(), boldFace, 14, Brushes.Black);
                dc.DrawText(dayT, new Point(x + (cellW - dayT.Width) / 2, colY + 4));

                var dowT = Fmt(col.DayOfWeekLabel, normalFace, 11, dowFg);
                dc.DrawText(dowT, new Point(x + (cellW - dowT.Width) / 2, colY + colH - dowT.Height - 5));
            }

            // ── 資料列 ───────────────────────────────────────────────────────
            for (int r = 0; r < data.Rows.Count; r++)
            {
                var row  = data.Rows[r];
                double y = titleH + colH + r * rowH;

                // 交替底色
                var rowBg = r % 2 == 0
                    ? Brushes.White
                    : (Brush)FreezeColor(Color.FromRgb(0xF6, 0xFA, 0xFD));
                dc.DrawRectangle(rowBg, null, new Rect(0, y, totalW, rowH));

                // 員工姓名格
                dc.DrawRectangle(null, pen10, new Rect(0, y, nameW, rowH));
                var nameT = Fmt(row.Name, semiBoldFace, 13, Brushes.Black);
                dc.DrawText(nameT, new Point(8, y + (rowH - nameT.Height) / 2));

                // 班次格（以顏色填滿）
                for (int c = 0; c < row.ShiftIds.Count && c < data.Columns.Count; c++)
                {
                    var col     = data.Columns[c];
                    var shiftId = row.ShiftIds[c];
                    var x       = nameW + c * cellW;

                    if (col.IsClosed)
                    {
                        // 店休：深灰格
                        dc.DrawRectangle(FreezeColor(Color.FromRgb(0xE0, 0xE0, 0xE0)), null, new Rect(x, y, cellW, rowH));
                        var closedT = Fmt("休", normalFace, 11, Brushes.Gray);
                        dc.DrawText(closedT, new Point(x + (cellW - closedT.Width) / 2, y + (rowH - closedT.Height) / 2));
                    }
                    else if (shiftId.HasValue && colorMap.TryGetValue(shiftId.Value, out var shiftColor))
                    {
                        // 有排班：以班別顏色塗滿（70% 不透明，保持視覺柔和）
                        var fill = new SolidColorBrush(Color.FromArgb(0xCC, shiftColor.R, shiftColor.G, shiftColor.B));
                        fill.Freeze();
                        dc.DrawRectangle(fill, null, new Rect(x, y, cellW, rowH));
                    }
                    // 未排班：保持列底色（空白）

                    dc.DrawRectangle(null, pen05, new Rect(x, y, cellW, rowH));
                }
            }

            // 表格外框
            var outerPen = FreezePen(Color.FromRgb(0x88, 0x99, 0xAA), 1.5);
            dc.DrawRectangle(null, outerPen, new Rect(0, titleH, totalW, tableH));

            // ── 圖例區 ───────────────────────────────────────────────────────
            if (data.ShiftLegend.Count > 0)
            {
                double legendY = titleH + tableH;

                // 圖例分隔線 + 背景
                dc.DrawRectangle(FreezeColor(Color.FromRgb(0xF4, 0xF8, 0xFC)), null,
                    new Rect(0, legendY, totalW, legendAreaH));
                dc.DrawRectangle(null, FreezePen(Color.FromRgb(0xCC, 0xDD, 0xEE), 1.0),
                    new Rect(0, legendY, totalW, legendAreaH));

                // 標題「班別說明」
                var legHdrT = Fmt("班別說明", semiBoldFace, 12, FreezeColor(Color.FromRgb(0x33, 0x55, 0x77)));
                dc.DrawText(legHdrT, new Point(12, legendY + legendPad - legHdrT.Height - 2));

                // 各班別圖例（橫排，自動換行）
                double legX = 12;
                double legItemY = legendY + legendPad;
                double legItemW = 180; // 每個圖例項目的寬度

                foreach (var leg in data.ShiftLegend)
                {
                    // 換行
                    if (legX + legItemW > totalW - 12)
                    {
                        legX  = 12;
                        legItemY += legendH;
                    }

                    // 色塊
                    var sc = ParseHex(leg.ColorHex);
                    var swatchBrush = new SolidColorBrush(sc); swatchBrush.Freeze();
                    dc.DrawRectangle(swatchBrush, FreezePen(Color.FromRgb(0x88, 0x88, 0x88), 0.5),
                        new Rect(legX, legItemY + (legendH - legendSwatchSize) / 2,
                            legendSwatchSize, legendSwatchSize));

                    // 班名 + 時間
                    var legText = Fmt($"{leg.Alias}  {leg.TimeRange}", normalFace, 12, Brushes.Black);
                    dc.DrawText(legText, new Point(legX + legendSwatchSize + 6,
                        legItemY + (legendH - legText.Height) / 2));

                    legX += legItemW;
                }
            }
        }

        visual.Transform = new ScaleTransform(scale, scale);
        var rtb = new RenderTargetBitmap(
            (int)(totalW * scale), (int)(totalH * scale),
            dpi, dpi, PixelFormats.Pbgra32);
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
