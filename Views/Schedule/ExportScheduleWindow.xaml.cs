using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ShopManager.Models;
using ShopManager.Services;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShopManager.Views.Schedule;

public partial class ExportScheduleWindow : Window
{
    private readonly ExportScheduleData _data;
    private RenderTargetBitmap? _bitmap;
    private readonly List<PushRecipientItem> _recipients = new();

    public ExportScheduleWindow(ExportScheduleData data)
    {
        InitializeComponent();
        _data = data;
        Title = $"{data.Year} 年 {data.Month:D2} 月  班表匯出";
        Loaded += (_, _) =>
        {
            _bitmap = RenderSchedule(data);
            PreviewImage.Source = _bitmap;
            SetupLinePushPanel(data);
        };
    }

    private void SetupLinePushPanel(ExportScheduleData data)
    {
        if (data.PushRecipients.Count == 0
            || string.IsNullOrEmpty(data.LineChannelAccessToken)
            || string.IsNullOrEmpty(data.LineWorkerUrl)
            || string.IsNullOrEmpty(data.LineWorkerApiKey))
            return;

        foreach (var r in data.PushRecipients)
            _recipients.Add(new PushRecipientItem(r));

        RecipientList.ItemsSource = _recipients;
        LinePushPanel.Visibility = Visibility.Visible;
    }

    private void RadioFullSchedule_Checked(object sender, RoutedEventArgs e)        => SetRecipientFilter(owner: true,  employee: true);
    private void RadioPersonalOnly_Checked(object sender, RoutedEventArgs e)        => SetRecipientFilter(owner: false, employee: true);
    private void RadioAllPersonalToOwner_Checked(object sender, RoutedEventArgs e)  => SetRecipientFilter(owner: true,  employee: false);

    private void SetRecipientFilter(bool owner, bool employee)
    {
        foreach (var r in _recipients)
            r.IsEnabled = r.Recipient.IsOwner ? owner : employee;
        if (RecipientList is null) return; // InitializeComponent 期間 Checked 事件提前觸發
        RecipientList.ItemsSource = null;
        RecipientList.ItemsSource = _recipients;
    }

    private void ToggleAll_Click(object sender, RoutedEventArgs e)
    {
        bool allSelected = _recipients.Where(r => r.IsEnabled).All(r => r.IsSelected);
        foreach (var r in _recipients.Where(r => r.IsEnabled))
            r.IsSelected = !allSelected;
        RecipientList.ItemsSource = null;
        RecipientList.ItemsSource = _recipients;
    }

    private async void PushLine_Click(object sender, RoutedEventArgs e)
    {
        bool isPersonal    = RadioPersonalOnly.IsChecked == true;
        bool isAllToOwner  = RadioAllPersonalToOwner.IsChecked == true;
        var selected       = _recipients.Where(r => r.IsSelected && r.IsEnabled).ToList();
        var targets        = isPersonal     ? selected.Where(r => !r.Recipient.IsOwner).ToList()
                           : isAllToOwner   ? selected.Where(r =>  r.Recipient.IsOwner).ToList()
                           : selected;

        var snackbar = App.Services.GetRequiredService<IAppSnackbarService>();
        if (targets.Count == 0)
        {
            snackbar.ShowWarning(
                isPersonal   ? "個人班表模式下須勾選至少一位員工（業主帳號不適用）"
              : isAllToOwner ? "全體個人班表模式下須勾選至少一位業主帳號"
              :                "請先勾選至少一位收件人");
            return;
        }

        string confirmMsg =
            isPersonal   ? $"確定要發送個人班表圖片給 {targets.Count} 位員工？"
          : isAllToOwner ? $"確定要將全體員工的個人班表彙整推播給 {targets.Count} 位業主？"
          :                $"確定要將本月完整班表圖片推播給 {selected.Count} 位收件人？";
        bool confirmed = await App.Services.GetRequiredService<IAppDialogService>()
            .ShowConfirmAsync("確定推播", confirmMsg, "確定推播", "取消");
        if (!confirmed) return;

        var pushBtn = (System.Windows.Controls.Button)sender;
        pushBtn.IsEnabled = false;

        var lineService = App.Services.GetRequiredService<LineService>();
        int ok;

        if (isPersonal)
        {
            // 個人班表：每位員工各渲染一張只含自己排班的圖片並推播（圖片訊息可轉傳，且欄寬依內容自動撐開不裁切）
            ok = 0;
            var keys = new List<string>();
            foreach (var r in targets)
            {
                var bytes = EncodePng(RenderPersonalSchedule(_data, r.Recipient));
                var uploaded = await lineService.UploadScheduleImageAsync(
                    _data.LineWorkerUrl!, _data.LineWorkerApiKey!, bytes);
                if (uploaded is null) continue;
                keys.Add(uploaded.Value.Key);
                if (await lineService.PushImageAsync(_data.LineChannelAccessToken!, r.Recipient.UserId, uploaded.Value.Url))
                    ok++;
            }
            ScheduleImageCleanup(lineService, keys);
        }
        else if (isAllToOwner)
        {
            // 全體個人班表：以 _data.Rows（全體在職員工）逐一渲染個人班表圖片，推給每位業主
            // 不受員工 LINE 綁定狀態影響 — 業主端等同於檢視全員班表卡；每位員工圖片只渲染/上傳一次，多位業主共用同一張
            var allEmployees = _data.Rows
                .Select(row => new ExportScheduleData.PushRecipient(
                    UserId: string.Empty,
                    DisplayName: row.Name,
                    PictureUrl: null,
                    IsOwner: false,
                    ShiftIds: row.ShiftIds))
                .ToList();
            if (allEmployees.Count == 0)
            {
                snackbar.ShowWarning("本月沒有員工資料");
                pushBtn.IsEnabled = true;
                return;
            }

            var employeeImages = new List<string>(); // image URLs
            var keys = new List<string>();
            foreach (var emp in allEmployees)
            {
                var bytes = EncodePng(RenderPersonalSchedule(_data, emp));
                var uploaded = await lineService.UploadScheduleImageAsync(
                    _data.LineWorkerUrl!, _data.LineWorkerApiKey!, bytes);
                if (uploaded is null) continue;
                employeeImages.Add(uploaded.Value.Url);
                keys.Add(uploaded.Value.Key);
            }

            ok = 0;
            foreach (var owner in targets)
            {
                bool allOk = employeeImages.Count > 0;
                foreach (var url in employeeImages)
                {
                    var success = await lineService.PushImageAsync(
                        _data.LineChannelAccessToken!, owner.Recipient.UserId, url);
                    if (!success) allOk = false;
                }
                if (allOk) ok++;
            }
            ScheduleImageCleanup(lineService, keys);
        }
        else
        {
            // 完整班表：所有收件人收到相同班表圖片
            if (_bitmap is null) { pushBtn.IsEnabled = true; return; }
            var uploaded = await lineService.UploadScheduleImageAsync(
                _data.LineWorkerUrl!, _data.LineWorkerApiKey!, EncodePng(_bitmap));
            if (uploaded is null)
            {
                snackbar.ShowError("圖片上傳失敗，請確認 Worker URL 與 API Key");
                pushBtn.IsEnabled = true;
                return;
            }
            var (imageUrl, imageKey) = uploaded.Value;

            ok = (await Task.WhenAll(
                selected.Select(r => lineService.PushImageAsync(
                    _data.LineChannelAccessToken!, r.Recipient.UserId, imageUrl))))
                .Count(r => r);

            ScheduleImageCleanup(lineService, new List<string> { imageKey });
        }

        if (ok == targets.Count)
            snackbar.ShowSuccess($"已成功推播給 {ok} 位收件人");
        else
            snackbar.ShowWarning($"推播完成：{ok}/{targets.Count} 位成功，可再次推播");

        // 不論成功與否都解鎖按鈕，避免使用者要再次推播時無法點擊
        pushBtn.IsEnabled = true;
    }

    /// <summary>將 PNG 位元組上傳前的編碼，供班表圖片（完整版／個人版）共用</summary>
    private static byte[] EncodePng(BitmapSource bmp)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>延遲清除已上傳的暫存班表圖片（best-effort，不等待結果）</summary>
    private void ScheduleImageCleanup(LineService lineService, List<string> keys)
    {
        if (keys.Count == 0) return;
        _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
        {
            foreach (var key in keys)
                _ = lineService.DeleteScheduleImageAsync(_data.LineWorkerUrl!, _data.LineWorkerApiKey!, key);
        });
    }

    /// <summary>
    /// 個人班表圖片渲染：只列出該員工本月有排班的日子，逐列直排。
    /// 欄寬依實際文字量測結果撐開（非固定比例），保證班別名稱／時間不被裁切；圖片訊息也可在 LINE 中轉傳。
    /// </summary>
    private static RenderTargetBitmap RenderPersonalSchedule(ExportScheduleData data, ExportScheduleData.PushRecipient recipient)
    {
        const double dpi   = 96;
        const double scale = 1.5;
        double S(double v) => v * scale;

        var fontFamily   = new FontFamily("Microsoft JhengHei UI, Microsoft JhengHei, sans-serif");
        var normalFace   = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal,   FontStretches.Normal);
        var boldFace     = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold,     FontStretches.Normal);
        var semiBoldFace = new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        var legendById = data.ShiftLegend.ToDictionary(l => l.Id);

        var rows = new List<(ExportScheduleData.DayColumn Col, ExportScheduleData.ShiftLegendItem Leg)>();
        if (recipient.ShiftIds is not null)
        {
            for (int i = 0; i < recipient.ShiftIds.Count && i < data.Columns.Count; i++)
            {
                var shiftId = recipient.ShiftIds[i];
                if (!shiftId.HasValue) continue;
                if (!legendById.TryGetValue(shiftId.Value, out var leg)) continue;
                rows.Add((data.Columns[i], leg));
            }
        }

        double titleH = S(56);
        double rowH   = S(40);
        double padL   = S(16);
        double padR   = S(16);
        double gap    = S(10);
        double badgePadH = S(10);
        double dateSize = S(14), badgeSize = S(13), timeSize = S(14);

        // 第一輪：量測各欄實際所需寬度，欄寬依內容分配，不用固定比例
        double dateW = 0, badgeW = 0, timeW = 0;
        foreach (var (col, leg) in rows)
        {
            var dateT  = Fmt($"{data.Month:D2}/{col.Day:D2} ({col.DayOfWeekLabel})", semiBoldFace, dateSize, Brushes.Black);
            var badgeT = Fmt(leg.Alias, boldFace, badgeSize, Brushes.White);
            var timeT  = Fmt(leg.TimeRange, normalFace, timeSize, Brushes.Black);
            dateW  = Math.Max(dateW, dateT.Width);
            badgeW = Math.Max(badgeW, badgeT.Width);
            timeW  = Math.Max(timeW, timeT.Width);
        }
        double badgeBoxW = badgeW + badgePadH * 2;

        double totalW = rows.Count == 0 ? S(300) : padL + dateW + gap + badgeBoxW + gap + timeW + padR;
        double bodyH  = rows.Count == 0 ? S(70) : rows.Count * rowH;
        double totalH = titleH + bodyH + 1;

        var lineDivider = FreezePen(Color.FromRgb(0xE5, 0xE5, 0xE5), 0.5);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));

            dc.DrawRectangle(FreezeColor(Color.FromRgb(0x2A, 0x5C, 0x8A)), null, new Rect(0, 0, totalW, titleH));
            var subT = Fmt($"{data.ShopName}　{data.Year}年{data.Month}月", normalFace, S(11), FreezeColor(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)));
            dc.DrawText(subT, new Point(padL, S(10)));
            var nameT = Fmt($"{recipient.DisplayName}　個人班表", boldFace, S(16), Brushes.White);
            dc.DrawText(nameT, new Point(padL, S(27)));

            if (rows.Count == 0)
            {
                var emptyT = Fmt("本月尚無排班紀錄", normalFace, S(13), FreezeColor(Color.FromRgb(0x88, 0x88, 0x88)));
                dc.DrawText(emptyT, new Point((totalW - emptyT.Width) / 2, titleH + (bodyH - emptyT.Height) / 2));
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var (col, leg) = rows[i];
                    double y = titleH + i * rowH;
                    dc.DrawRectangle(i % 2 == 0 ? Brushes.White : (Brush)FreezeColor(Color.FromRgb(0xF6, 0xFA, 0xFD)),
                        null, new Rect(0, y, totalW, rowH));

                    double x = padL;
                    var dateT = Fmt($"{data.Month:D2}/{col.Day:D2} ({col.DayOfWeekLabel})", semiBoldFace, dateSize, Brushes.Black);
                    dc.DrawText(dateT, new Point(x, y + (rowH - dateT.Height) / 2));
                    x += dateW + gap;

                    var badgeBrush = new SolidColorBrush(ParseHex(leg.ColorHex)); badgeBrush.Freeze();
                    double badgeBoxH = S(24);
                    dc.DrawRoundedRectangle(badgeBrush, null,
                        new Rect(x, y + (rowH - badgeBoxH) / 2, badgeBoxW, badgeBoxH), S(4), S(4));
                    var badgeT = Fmt(leg.Alias, boldFace, badgeSize, Brushes.White);
                    dc.DrawText(badgeT, new Point(x + (badgeBoxW - badgeT.Width) / 2, y + (rowH - badgeT.Height) / 2));
                    x += badgeBoxW + gap;

                    var timeT = Fmt(leg.TimeRange, normalFace, timeSize, FreezeColor(Color.FromRgb(0x33, 0x44, 0x55)));
                    dc.DrawText(timeT, new Point(x, y + (rowH - timeT.Height) / 2));

                    dc.DrawLine(lineDivider, new Point(0, y + rowH), new Point(totalW, y + rowH));
                }
            }
        }

        var rtb = new RenderTargetBitmap((int)totalW, (int)totalH, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
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

    // ── 收件人項目（供 ItemsControl DataTemplate 繫結）────────────────────────
    public sealed class PushRecipientItem
    {
        private static readonly SolidColorBrush EmployeeBrush;
        private static readonly SolidColorBrush OwnerBrush;

        static PushRecipientItem()
        {
            EmployeeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)); EmployeeBrush.Freeze();
            OwnerBrush    = new SolidColorBrush(Color.FromRgb(0x3D, 0xAA, 0x70)); OwnerBrush.Freeze();
        }

        public PushRecipientItem(ExportScheduleData.PushRecipient r) => Recipient = r;

        public ExportScheduleData.PushRecipient Recipient { get; }
        public bool IsSelected { get; set; } = true;
        public bool IsEnabled  { get; set; } = true;

        public string DisplayName => Recipient.DisplayName;
        public string SourceLabel => Recipient.IsOwner ? "業主" : "員工";
        public string Initial     => string.IsNullOrEmpty(Recipient.DisplayName)
                                     ? "?" : Recipient.DisplayName[0].ToString();
        public SolidColorBrush CircleBrush => Recipient.IsOwner ? OwnerBrush : EmployeeBrush;
    }

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
        var legendById = data.ShiftLegend.ToDictionary(l => l.Id);

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
                        var fill = new SolidColorBrush(Color.FromArgb(0xFF, sc.R, sc.G, sc.B));
                        fill.Freeze();
                        dc.DrawRectangle(fill, null, new Rect(x, y, cellW, rowH));

                        // 班別起訖時間塞進色塊內（兩列，小字）
                        if (legendById.TryGetValue(shiftId.Value, out var leg)
                            && !string.IsNullOrEmpty(leg.TimeRange))
                        {
                            var parts = leg.TimeRange.Split('–', '-', '~');
                            double ts = S(9);
                            if (parts.Length >= 2)
                            {
                                var t1 = Fmt(parts[0].Trim(), boldFace, ts, Brushes.White);
                                var t2 = Fmt(parts[1].Trim(), boldFace, ts, Brushes.White);
                                double ty = y + (rowH - t1.Height - t2.Height) / 2;
                                dc.DrawText(t1, new Point(x + (cellW - t1.Width) / 2, ty));
                                dc.DrawText(t2, new Point(x + (cellW - t2.Width) / 2, ty + t1.Height));
                            }
                            else
                            {
                                var t1 = Fmt(leg.TimeRange, boldFace, ts, Brushes.White);
                                dc.DrawText(t1, new Point(x + (cellW - t1.Width) / 2,
                                    y + (rowH - t1.Height) / 2));
                            }
                        }
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
