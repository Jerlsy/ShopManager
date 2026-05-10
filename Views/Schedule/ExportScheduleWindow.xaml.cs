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

    // ── 班表圖片渲染（DrawingVisual → RenderTargetBitmap）──────────────────
    private static RenderTargetBitmap RenderSchedule(ExportScheduleData data)
    {
        const double dpi    = 96;
        const double nameW  = 90;
        const double cellW  = 44;
        const double titleH = 44;
        const double colH   = 46;
        const double rowH   = 34;

        double totalW = nameW + data.DaysInMonth * cellW;
        double totalH = titleH + colH + data.Rows.Count * rowH + 1;

        var fontFamily  = new FontFamily("Microsoft JhengHei UI, Microsoft JhengHei, sans-serif");
        var normalFace  = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var boldFace    = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold,   FontStretches.Normal);
        var semiBoldFace= new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold,FontStretches.Normal);

        // 預先凍結畫筆以提升效能
        var pen05 = new Pen(new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), 0.5); pen05.Freeze();
        var pen10 = new Pen(new SolidColorBrush(Color.FromRgb(0x99, 0xAA, 0xBB)), 1.0); pen10.Freeze();

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 白色底
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));

            // ── 標題列 ──────────────────────────────────────────────────────
            var titleBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x5C, 0x8A)); titleBg.Freeze();
            dc.DrawRectangle(titleBg, null, new Rect(0, 0, totalW, titleH));
            var titleT = Fmt($"{data.ShopName}　{data.Year} 年 {data.Month} 月　班表", boldFace, 15, Brushes.White);
            dc.DrawText(titleT, new Point(14, (titleH - titleT.Height) / 2));

            // ── 欄位標題列 ───────────────────────────────────────────────────
            var colBg = new SolidColorBrush(Color.FromRgb(0xE3, 0xEE, 0xF7)); colBg.Freeze();
            dc.DrawRectangle(colBg, null, new Rect(0, titleH, totalW, colH));

            // 員工名稱欄標題
            dc.DrawRectangle(null, pen10, new Rect(0, titleH, nameW, colH));
            var nameHdrT = Fmt("員工", semiBoldFace, 12, new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0x88)));
            dc.DrawText(nameHdrT, new Point((nameW - nameHdrT.Width) / 2, titleH + (colH - nameHdrT.Height) / 2));

            // 各日欄標題
            for (int i = 0; i < data.Columns.Count; i++)
            {
                var col = data.Columns[i];
                var x   = nameW + i * cellW;

                Brush colCellBg;
                Brush dowFg;
                if (col.IsClosed)
                {
                    colCellBg = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));
                    dowFg     = Brushes.Gray;
                }
                else if (col.DayOfWeekLabel == "日")
                {
                    colCellBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xE3, 0xE3));
                    dowFg     = Brushes.Crimson;
                }
                else if (col.DayOfWeekLabel == "六")
                {
                    colCellBg = new SolidColorBrush(Color.FromRgb(0xE3, 0xEE, 0xFF));
                    dowFg     = Brushes.RoyalBlue;
                }
                else
                {
                    colCellBg = colBg;
                    dowFg     = new SolidColorBrush(Color.FromRgb(0x44, 0x55, 0x66));
                }

                dc.DrawRectangle(colCellBg, null, new Rect(x, titleH, cellW, colH));
                dc.DrawRectangle(null, pen05, new Rect(x, titleH, cellW, colH));

                var dayT = Fmt(col.Day.ToString(), boldFace, 14, Brushes.Black);
                dc.DrawText(dayT, new Point(x + (cellW - dayT.Width) / 2, titleH + 4));

                var dowT = Fmt(col.DayOfWeekLabel, normalFace, 11, dowFg);
                dc.DrawText(dowT, new Point(x + (cellW - dowT.Width) / 2, titleH + colH - dowT.Height - 5));
            }

            // ── 資料列 ───────────────────────────────────────────────────────
            for (int r = 0; r < data.Rows.Count; r++)
            {
                var row  = data.Rows[r];
                double y = titleH + colH + r * rowH;

                var rowBg = r % 2 == 0
                    ? Brushes.White
                    : (Brush)new SolidColorBrush(Color.FromRgb(0xF6, 0xFA, 0xFD));
                dc.DrawRectangle(rowBg, null, new Rect(0, y, totalW, rowH));

                // 員工姓名格
                dc.DrawRectangle(null, pen10, new Rect(0, y, nameW, rowH));
                var nameT = Fmt(row.Name, semiBoldFace, 13, Brushes.Black);
                dc.DrawText(nameT, new Point(8, y + (rowH - nameT.Height) / 2));

                // 班次格
                for (int c = 0; c < row.Cells.Count && c < data.Columns.Count; c++)
                {
                    var col      = data.Columns[c];
                    var cellText = row.Cells[c];
                    var x        = nameW + c * cellW;

                    Brush? cellBg = null;
                    Brush  cellFg = Brushes.Black;

                    if (col.IsClosed)
                    {
                        cellBg = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
                        cellFg = Brushes.Gray;
                    }
                    else if (!string.IsNullOrEmpty(cellText))
                    {
                        if (col.DayOfWeekLabel == "日")
                            cellBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF0, 0xF0));
                        else if (col.DayOfWeekLabel == "六")
                            cellBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xFF));
                    }

                    if (cellBg is not null) dc.DrawRectangle(cellBg, null, new Rect(x, y, cellW, rowH));
                    dc.DrawRectangle(null, pen05, new Rect(x, y, cellW, rowH));

                    if (!string.IsNullOrEmpty(cellText))
                    {
                        var ct = Fmt(cellText, normalFace, 13, cellFg);
                        dc.DrawText(ct, new Point(x + (cellW - ct.Width) / 2, y + (rowH - ct.Height) / 2));
                    }
                }
            }

            // 最外框
            var outerPen = new Pen(new SolidColorBrush(Color.FromRgb(0x99, 0xAA, 0xBB)), 1.5); outerPen.Freeze();
            dc.DrawRectangle(null, outerPen, new Rect(0, titleH, totalW, totalH - titleH));
        }

        var rtb = new RenderTargetBitmap((int)totalW, (int)totalH, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }

    private static FormattedText Fmt(string text, Typeface typeface, double size, Brush fg) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, fg, 1.0);
}
