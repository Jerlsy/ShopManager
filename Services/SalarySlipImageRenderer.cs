using ShopManager.Models;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShopManager.Services;

/// <summary>
/// 薪資單 LINE 推播圖片渲染。原本以 Flex Message 呈現，但 LINE 的 Flex 訊息無法轉傳
/// （與班表推播同一限制，見 <see cref="LineService"/> 相關說明），改為圖片訊息並比照
/// Views/Schedule/ExportScheduleWindow 既有的 DrawingVisual/FormattedText 繪圖方式，
/// 全面改用單一圖片格式維護。
/// </summary>
public static class SalarySlipImageRenderer
{
    private enum RowKind { Text, Divider, Emphasis }

    private readonly record struct SlipRow(RowKind Kind, string Label, string Value, Color? ValueColor, bool ThickDivider)
    {
        public static SlipRow Text(string label, string value, Color? valueColor = null) =>
            new(RowKind.Text, label, value, valueColor, false);
        public static SlipRow Divider(bool thick = false) =>
            new(RowKind.Divider, "", "", null, thick);
        public static SlipRow Emphasis(string label, string value) =>
            new(RowKind.Emphasis, label, value, null, false);
    }

    /// <summary>渲染並直接編碼為 PNG bytes，供上傳/推播使用。</summary>
    public static byte[] RenderPng(SalaryEmployeeRecord r, int year, int month, DateTime? paidAt, string shopName)
    {
        var bmp = Render(r, year, month, paidAt, shopName);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static RenderTargetBitmap Render(SalaryEmployeeRecord r, int year, int month, DateTime? paidAt, string shopName)
    {
        const double dpi = 96;
        const double scale = 1.5;
        double S(double v) => v * scale;

        var fontFamily   = new FontFamily("Microsoft JhengHei UI, Microsoft JhengHei, sans-serif");
        var normalFace   = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal,   FontStretches.Normal);
        var boldFace     = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold,     FontStretches.Normal);
        var semiBoldFace = new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        var deductionColor = Color.FromRgb(0xE5, 0x39, 0x35);
        var labelColor      = Color.FromRgb(0x88, 0x88, 0x88);
        var valueColor      = Color.FromRgb(0x22, 0x22, 0x22);
        var emphasisColor   = Color.FromRgb(0x4A, 0x90, 0xD9);
        var dividerColor    = Color.FromRgb(0xE2, 0xE2, 0xE2);

        // ── 組出要畫的列（比照原 BuildSalarySlipFlex 的欄位與順序）──────────
        var rows = new List<SlipRow>
        {
            SlipRow.Text("員工", r.Employee.Name),
            SlipRow.Text("制度", r.SalaryType == SalaryType.Hourly ? "時薪制" : "月薪制"),
            SlipRow.Divider(),
        };

        if (r.SalaryType == SalaryType.Hourly)
        {
            rows.Add(SlipRow.Text("平日工時", $"{r.WeekdayHours:N1} hr"));
            if (r.HolidayHours > 0) rows.Add(SlipRow.Text("假日工時", $"{r.HolidayHours:N1} hr"));
            if (r.OT1Hours > 0)     rows.Add(SlipRow.Text("加班一段", $"{r.OT1Hours:N1} hr"));
            if (r.OT2Hours > 0)     rows.Add(SlipRow.Text("加班二段", $"{r.OT2Hours:N1} hr"));
            rows.Add(SlipRow.Text("平日薪資", $"${r.WeekdayPay:N0}"));
            if (r.HolidayPay > 0) rows.Add(SlipRow.Text("假日薪資", $"${r.HolidayPay:N0}"));
            if (r.OT1Pay + r.OT2Pay > 0) rows.Add(SlipRow.Text("加班費", $"${r.OT1Pay + r.OT2Pay:N0}"));
        }
        else
        {
            rows.Add(SlipRow.Text("底薪", $"${r.WeekdayPay:N0}"));
            if (r.HolidayPay > 0)        rows.Add(SlipRow.Text("假日薪資", $"${r.HolidayPay:N0}"));
            if (r.OT1Pay + r.OT2Pay > 0) rows.Add(SlipRow.Text("加班費",   $"${r.OT1Pay + r.OT2Pay:N0}"));
        }

        if (r.OverridePay != 0)
            rows.Add(SlipRow.Text("特殊薪資", $"${r.OverridePay:N0}"));

        if (r.BonusItems.Count > 0)
        {
            rows.Add(SlipRow.Divider());
            foreach (var b in r.BonusItems)
                rows.Add(SlipRow.Text(b.Label, $"{(b.Amount >= 0 ? "+" : "")}${b.Amount:N0}",
                    b.Amount >= 0 ? valueColor : deductionColor));
        }

        var grand = r.BaseAmount + r.BonusItems.Sum(b => b.Amount);
        rows.Add(SlipRow.Divider(thick: true));
        rows.Add(SlipRow.Emphasis("應領薪資", $"${grand:N0}"));

        if (paidAt.HasValue)
        {
            rows.Add(SlipRow.Divider());
            rows.Add(SlipRow.Text("支薪日期", paidAt.Value.ToString("yyyy/MM/dd")));
        }

        // ── 版面尺寸 ─────────────────────────────────────────────────────
        double titleH    = S(64);
        double padH      = S(20);
        double padTop    = S(16);
        double padBottom = S(18);
        double rowH      = S(30);
        double dividerH  = S(14);
        double emphasisH = S(44);

        double RowHeight(SlipRow row) => row.Kind switch
        {
            RowKind.Divider  => dividerH,
            RowKind.Emphasis => emphasisH,
            _                => rowH,
        };

        double bodyH  = rows.Sum(RowHeight);
        double totalW = S(320);
        double totalH = titleH + padTop + bodyH + padBottom;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));

            // 標題列
            dc.DrawRectangle(FreezeColor(emphasisColor), null, new Rect(0, 0, totalW, titleH));
            var subT = Fmt(shopName, normalFace, S(11), FreezeColor(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
            dc.DrawText(subT, new Point(padH, S(12)));
            var titleT = Fmt($"{year}年{month}月　薪資單", boldFace, S(17), Brushes.White);
            dc.DrawText(titleT, new Point(padH, S(30)));

            double y = titleH + padTop;
            foreach (var row in rows)
            {
                double h = RowHeight(row);
                switch (row.Kind)
                {
                    case RowKind.Divider:
                        var pen = FreezePen(dividerColor, row.ThickDivider ? 1.3 : 0.6);
                        double ly = y + h / 2;
                        dc.DrawLine(pen, new Point(padH, ly), new Point(totalW - padH, ly));
                        break;

                    case RowKind.Emphasis:
                        var lblE = Fmt(row.Label, boldFace, S(15), FreezeColor(valueColor));
                        var valE = Fmt(row.Value, boldFace, S(16), FreezeColor(emphasisColor));
                        dc.DrawText(lblE, new Point(padH, y + (h - lblE.Height) / 2));
                        dc.DrawText(valE, new Point(totalW - padH - valE.Width, y + (h - valE.Height) / 2));
                        break;

                    default:
                        var lblT = Fmt(row.Label, normalFace, S(12.5), FreezeColor(labelColor));
                        var valT = Fmt(row.Value, semiBoldFace, S(12.5), FreezeColor(row.ValueColor ?? valueColor));
                        dc.DrawText(lblT, new Point(padH, y + (h - lblT.Height) / 2));
                        dc.DrawText(valT, new Point(totalW - padH - valT.Width, y + (h - valT.Height) / 2));
                        break;
                }
                y += h;
            }
        }

        var rtb = new RenderTargetBitmap((int)totalW, (int)totalH, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
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
