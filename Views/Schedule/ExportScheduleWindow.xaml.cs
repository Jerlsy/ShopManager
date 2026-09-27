using CommunityToolkit.Mvvm.ComponentModel;
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
    // 全體班表拆上半月／下半月兩張圖（A4 橫式）
    private RenderTargetBitmap? _bitmapTop;
    private RenderTargetBitmap? _bitmapBottom;
    private readonly List<PushRecipientItem> _recipients = new();

    // 推播進行中若使用者關閉視窗：攔截關閉動作、詢問是否中止，避免使用者以為關閉=取消，
    // 結果背景仍悄悄繼續送出（Task 不會因為視窗關閉而被取消）。
    private bool _isPushing;
    private CancellationTokenSource? _pushCts;

    public ExportScheduleWindow(ExportScheduleData data)
    {
        InitializeComponent();
        _data = data;
        Title = $"{data.Year} 年 {data.Month:D2} 月  班表匯出";
        Loaded += (_, _) =>
        {
            int split = HalfSplitIndex(data.DaysInMonth);
            _bitmapTop    = RenderScheduleRange(data, 0, split, "上半月");
            _bitmapBottom = RenderScheduleRange(data, split, data.DaysInMonth, "下半月");
            PreviewTopImage.Source    = _bitmapTop;
            PreviewBottomImage.Source = _bitmapBottom;
            SetupLinePushPanel(data);
        };
        Closing += Window_Closing;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isPushing) return; // 沒有進行中的推播，正常關閉

        e.Cancel = true; // 先攔下，問清楚意圖後再決定是否真的關閉
        // 這裡用 MessageBox 而非 IAppDialogService：本視窗以 ShowDialog 開啟，
        // MaterialDesign 的 RootDialog 只掛在 MainWindow 上，會被壓在本視窗底下看不到（見開發紀錄）。
        var result = MessageBox.Show(
            "目前還有排隊中的推播尚未送出，確定要中止並關閉視窗嗎？已送出的訊息不會被收回。",
            "推播尚未完成", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _pushCts?.Cancel();
        _isPushing = false;
        Close();
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

    private void ToggleAll_Click(object sender, RoutedEventArgs e)
    {
        bool allSelected = _recipients.Where(r => r.IsEnabled).All(r => r.IsSelected);
        foreach (var r in _recipients.Where(r => r.IsEnabled))
            r.IsSelected = !allSelected;
    }


    private async void PushLine_Click(object sender, RoutedEventArgs e)
    {
        bool isPersonal = RadioPersonalOnly.IsChecked == true;
        var selected    = _recipients.Where(r => r.IsSelected && r.IsEnabled).ToList();

        var snackbar = App.Services.GetRequiredService<IAppSnackbarService>();
        if (selected.Count == 0)
        {
            snackbar.ShowWarning("請先勾選至少一位收件人");
            return;
        }

        string confirmMsg = isPersonal
            ? $"確定要推播個人班表給 {selected.Count} 位收件人？（業主／群組會收到全部員工的個人班表，員工只收到自己的）"
            : $"確定要將本月完整班表圖片推播給 {selected.Count} 位收件人？";
        // 用 MessageBox：本視窗以 ShowDialog 開啟，MaterialDesign 的 RootDialog 掛在 MainWindow 會被壓在底下看不到
        var confirmResult = MessageBox.Show(confirmMsg, "確定推播", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmResult != MessageBoxResult.Yes) return;

        var pushBtn = (System.Windows.Controls.Button)sender;
        pushBtn.IsEnabled = false;
        ToggleAllButton.IsEnabled = false;
        ShowPushProgress();

        _isPushing = true;
        _pushCts   = new CancellationTokenSource();
        var token  = _pushCts.Token;

        var lineService = App.Services.GetRequiredService<LineService>();
        int ok = 0;

        try
        {
            if (isPersonal)
            {
                // 個人班表：
                //   業主／群組 → 無條件收到「全體員工」的個人班表（不受員工 LINE 綁定狀態影響）
                //   員工 → 只收到自己的個人班表
                // 圖片訊息可在 LINE 轉傳，且欄寬依內容自動撐開不裁切。
                var keys = new List<string>();

                // 只要有業主或群組收件人，先把全體員工（_data.Rows）的個人班表各渲染／上傳一次，多方共用同一批 URL
                List<string>? allEmployeeUrls = null;
                if (selected.Any(r => r.Recipient.Kind is ExportScheduleData.PushRecipientKind.Owner
                                                        or ExportScheduleData.PushRecipientKind.Group))
                {
                    allEmployeeUrls = new List<string>();
                    for (int i = 0; i < _data.Rows.Count; i++)
                    {
                        if (token.IsCancellationRequested) break;
                        SetPushProgress("渲染員工班表圖片", i, _data.Rows.Count);
                        var row = _data.Rows[i];
                        var rec = new ExportScheduleData.PushRecipient(
                            UserId: string.Empty, DisplayName: row.Name, PictureUrl: null,
                            Kind: ExportScheduleData.PushRecipientKind.Employee, ShiftIds: row.ShiftIds);
                        var uploaded = await lineService.UploadScheduleImageAsync(
                            _data.LineWorkerUrl!, _data.LineWorkerApiKey!,
                            EncodePng(RenderPersonalSchedule(_data, rec)));
                        if (uploaded is null) continue;
                        allEmployeeUrls.Add(uploaded.Value.Url);
                        keys.Add(uploaded.Value.Key);
                    }
                }

                int done = 0;
                foreach (var t in selected)
                {
                    if (token.IsCancellationRequested) break;

                    if (t.Recipient.Kind is ExportScheduleData.PushRecipientKind.Owner
                                          or ExportScheduleData.PushRecipientKind.Group)
                    {
                        // 業主／群組：把全體員工的個人班表逐張推過去
                        bool allOk = allEmployeeUrls is { Count: > 0 };
                        if (allEmployeeUrls is not null)
                        {
                            SetPushProgress("推播給業主／群組", done, selected.Count);
                            foreach (var url in allEmployeeUrls)
                            {
                                if (token.IsCancellationRequested) break;
                                if (!await lineService.PushImageAsync(
                                        _data.LineChannelAccessToken!, t.Recipient.UserId, url))
                                    allOk = false;
                            }
                        }
                        if (allOk) ok++;
                    }
                    else
                    {
                        // 員工：只推自己的個人班表
                        SetPushProgress("推播個人班表", done, selected.Count);
                        var uploaded = await lineService.UploadScheduleImageAsync(
                            _data.LineWorkerUrl!, _data.LineWorkerApiKey!,
                            EncodePng(RenderPersonalSchedule(_data, t.Recipient)));
                        if (uploaded is not null)
                        {
                            keys.Add(uploaded.Value.Key);
                            if (await lineService.PushImageAsync(
                                    _data.LineChannelAccessToken!, t.Recipient.UserId, uploaded.Value.Url))
                                ok++;
                        }
                    }
                    done++;
                }

                ScheduleImageCleanup(lineService, keys);
            }
            else
            {
                // 完整班表：所有收件人收到相同的上半月＋下半月兩張圖片
                if (_bitmapTop is null || _bitmapBottom is null) return;

                SetPushProgress("上傳班表圖片（上半月）", 0, 0);
                var upTop = await lineService.UploadScheduleImageAsync(
                    _data.LineWorkerUrl!, _data.LineWorkerApiKey!, EncodePng(_bitmapTop));
                SetPushProgress("上傳班表圖片（下半月）", 0, 0);
                var upBottom = await lineService.UploadScheduleImageAsync(
                    _data.LineWorkerUrl!, _data.LineWorkerApiKey!, EncodePng(_bitmapBottom));
                if (upTop is null || upBottom is null)
                {
                    snackbar.ShowError("圖片上傳失敗，請確認 Worker URL 與 API Key");
                    return;
                }

                for (int i = 0; i < selected.Count; i++)
                {
                    if (token.IsCancellationRequested) break;
                    SetPushProgress("推播給收件人", i, selected.Count);
                    var uid = selected[i].Recipient.UserId;
                    // 兩張皆送達才算成功；上半月先送、下半月後送，收件人依序看到
                    bool a = await lineService.PushImageAsync(_data.LineChannelAccessToken!, uid, upTop.Value.Url);
                    bool b = await lineService.PushImageAsync(_data.LineChannelAccessToken!, uid, upBottom.Value.Url);
                    if (a && b) ok++;
                }

                ScheduleImageCleanup(lineService, new List<string> { upTop.Value.Key, upBottom.Value.Key });
            }

            if (token.IsCancellationRequested)
                snackbar.ShowWarning($"已中止推播：成功送出 {ok} 位，其餘已取消未送出");
            else if (ok == selected.Count)
                snackbar.ShowSuccess($"已成功推播給 {ok} 位收件人");
            else
                snackbar.ShowWarning($"推播完成：{ok}/{selected.Count} 位成功，可再次推播");
        }
        finally
        {
            _isPushing = false;
            _pushCts?.Dispose();
            _pushCts = null;

            // 不論成功與否都解鎖按鈕，避免使用者要再次推播時無法點擊
            pushBtn.IsEnabled = true;
            ToggleAllButton.IsEnabled = true;
            HidePushProgress();
        }
    }

    private void ShowPushProgress() => PushProgressPanel.Visibility = Visibility.Visible;

    private void HidePushProgress() => PushProgressPanel.Visibility = Visibility.Collapsed;

    /// <summary>更新推播進度條；total=0 時顯示不確定進度（例如單次圖片上傳，無法拆步驟）。</summary>
    private void SetPushProgress(string phase, int done, int total)
    {
        if (total <= 0)
        {
            PushProgressBar.IsIndeterminate = true;
            PushProgressText.Text = $"{phase}…";
        }
        else
        {
            PushProgressBar.IsIndeterminate = false;
            PushProgressBar.Maximum = total;
            PushProgressBar.Value = done;
            PushProgressText.Text = $"{phase}… {done}/{total}";
        }
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
    internal static RenderTargetBitmap RenderPersonalSchedule(ExportScheduleData data, ExportScheduleData.PushRecipient recipient)
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
        if (_bitmapTop is null || _bitmapBottom is null) return;
        // 使用者選一個基準檔名：完整班表輸出 _上半月/_下半月 兩檔，
        // 另將全部員工的個人班表各存一張（{年}_{月}_{姓名}.png）
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 圖片|*.png",
            FileName = $"班表_{_data.Year}{_data.Month:D2}",
            DefaultExt = "png"
        };
        if (dlg.ShowDialog() != true) return;

        var dir  = Path.GetDirectoryName(dlg.FileName) ?? "";
        var name = Path.GetFileNameWithoutExtension(dlg.FileName);
        SavePng(_bitmapTop,    Path.Combine(dir, $"{name}_上半月.png"));
        SavePng(_bitmapBottom, Path.Combine(dir, $"{name}_下半月.png"));

        int personal = 0;
        foreach (var row in _data.Rows)
        {
            var rec = new ExportScheduleData.PushRecipient(
                UserId: string.Empty, DisplayName: row.Name, PictureUrl: null,
                Kind: ExportScheduleData.PushRecipientKind.Employee, ShiftIds: row.ShiftIds);
            var safeName = string.Join("_", row.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            SavePng(RenderPersonalSchedule(_data, rec),
                Path.Combine(dir, $"{_data.Year}_{_data.Month:D2}_{safeName}.png"));
            personal++;
        }

        App.Services.GetRequiredService<IAppSnackbarService>()
            .ShowSuccess($"已儲存完整班表 2 張與個人班表 {personal} 張至 {dir}");
    }

    private static void SavePng(BitmapSource bmp, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    private void IbonPrint_Click(object sender, RoutedEventArgs e)
    {
        if (_bitmapTop is null || _bitmapBottom is null) return;

        // 上傳完成後要把列印碼推播給業主 LINE，因此業主帳號與 LINE 設定為必要條件
        var snackbar = App.Services.GetRequiredService<IAppSnackbarService>();
        bool lineConfigured = !string.IsNullOrEmpty(_data.LineChannelAccessToken)
                           && !string.IsNullOrEmpty(_data.LineWorkerUrl)
                           && !string.IsNullOrEmpty(_data.LineWorkerApiKey);
        bool hasOwner = _data.PushRecipients.Any(r =>
            r.Kind == ExportScheduleData.PushRecipientKind.Owner && !string.IsNullOrEmpty(r.UserId));
        if (!lineConfigured || !hasOwner)
        {
            snackbar.ShowWarning(!lineConfigured
                ? "尚未完成 LINE 推播設定（Token / Worker），無法使用 ibon 雲端列印"
                : "尚未綁定業主 LINE 帳號，請先於系統設定綁定業主帳號");
            return;
        }

        var win = new IbonPrintWindow(_data, _bitmapTop, _bitmapBottom) { Owner = this };
        win.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ── 收件人項目（供 ItemsControl DataTemplate 繫結）────────────────────────
    // 一定要是 ObservableObject：IsSelected/IsEnabled 改了要讓畫面上的 CheckBox 即時反映，
    // 不能只靠「把 ItemsSource 設 null 再設回去」逼 WPF 重新產生容器——那個做法不可靠，
    // 曾經導致個人班表模式下已經停用的群組項目，畫面上還是顯示成可以勾選。
    public sealed partial class PushRecipientItem : ObservableObject
    {
        private static readonly SolidColorBrush EmployeeBrush;
        private static readonly SolidColorBrush OwnerBrush;
        private static readonly SolidColorBrush GroupBrush;

        static PushRecipientItem()
        {
            EmployeeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9)); EmployeeBrush.Freeze();
            OwnerBrush    = new SolidColorBrush(Color.FromRgb(0x3D, 0xAA, 0x70)); OwnerBrush.Freeze();
            GroupBrush    = new SolidColorBrush(Color.FromRgb(0x9C, 0x7A, 0x3D)); GroupBrush.Freeze();
        }

        public PushRecipientItem(ExportScheduleData.PushRecipient r) => Recipient = r;

        public ExportScheduleData.PushRecipient Recipient { get; }
        [ObservableProperty] private bool _isSelected = true;
        [ObservableProperty] private bool _isEnabled = true;

        public string DisplayName => Recipient.DisplayName;
        public string SourceLabel => Recipient.Kind switch
        {
            ExportScheduleData.PushRecipientKind.Owner => "業主",
            ExportScheduleData.PushRecipientKind.Group => "群組",
            _ => "員工",
        };
        public string Initial     => string.IsNullOrEmpty(Recipient.DisplayName)
                                     ? "?" : Recipient.DisplayName[0].ToString();
        public SolidColorBrush CircleBrush => Recipient.Kind switch
        {
            ExportScheduleData.PushRecipientKind.Owner => OwnerBrush,
            ExportScheduleData.PushRecipientKind.Group => GroupBrush,
            _ => EmployeeBrush,
        };
    }

    /// <summary>本月拆上／下半月的分界欄索引（上半月＝索引 [0, split)，下半月＝[split, 月天數)）。</summary>
    private static int HalfSplitIndex(int daysInMonth) => (daysInMonth + 1) / 2;

    // ─────────────────────────────────────────────────────────────────────────
    // 班表圖片渲染（指定日期區段，供上半月／下半月各出一張 A4 橫式圖）
    //   ‧ 版面以「單位尺寸」定義，最後依 A4 橫式畫布(1754×1240 @150dpi)反推等比 scale，
    //     整體放大→字體由 WPF 字型引擎在目標解析度原生渲染，絕不裁切。
    //   ‧ 表格頭尾各有一排欄標題（日期／星期），每張左側皆保留員工姓名欄。
    // ─────────────────────────────────────────────────────────────────────────
    internal static RenderTargetBitmap RenderScheduleRange(
        ExportScheduleData data, int startIndex, int endIndexExclusive, string rangeLabel)
    {
        const double dpi = 96;
        int numDays = Math.Max(0, Math.Min(endIndexExclusive, data.Columns.Count) - startIndex);

        // ── 單位版面常數（scale=1）──────────────────────────────────────────
        const double nameW_u = 90, cellW_u = 44, titleH_u = 44, colH_u = 46, rowH_u = 34;
        const double legTopGap_u = 20, legPadV_u = 12, legTitleRowH_u = 22, legTitleGap_u = 6,
                     legItemH_u = 28, legItemW_u = 180;

        int legendCount  = data.ShiftLegend.Count;
        double tableW_u  = nameW_u + numDays * cellW_u;
        int itemsPerRow  = legendCount == 0 ? 1 : Math.Max(1, (int)((tableW_u - 24) / legItemW_u));
        int itemRowCount = legendCount == 0 ? 0 : (legendCount + itemsPerRow - 1) / itemsPerRow;
        double legBoxH_u = legendCount > 0
            ? legPadV_u + legTitleRowH_u + legTitleGap_u + itemRowCount * legItemH_u + legPadV_u : 0;
        double legAreaH_u = legendCount > 0 ? legTopGap_u + legBoxH_u : 0;
        // 表格含上下兩排欄標題（頭尾都有日期）
        double tableH_u   = colH_u + data.Rows.Count * rowH_u + colH_u;
        double totalW_u   = tableW_u;
        double totalH_u   = titleH_u + tableH_u + legAreaH_u + 1;

        // A4 橫式 @150dpi ≈ 1754×1240，取等比縮放塞入畫布（不強制填滿高，避免變形），並夾在合理清晰度區間
        const double targetW = 1754, targetH = 1240, minScale = 1.3, maxScale = 2.6;
        double scale = Math.Clamp(Math.Min(targetW / totalW_u, targetH / totalH_u), minScale, maxScale);
        double S(double v) => v * scale;

        // ── 縮放後的實際尺寸 ────────────────────────────────────────────────
        double nameW = S(nameW_u), cellW = S(cellW_u), titleH = S(titleH_u), colH = S(colH_u), rowH = S(rowH_u);
        double legTopGap = S(legTopGap_u), legPadV = S(legPadV_u), legTitleRowH = S(legTitleRowH_u),
               legTitleGap = S(legTitleGap_u), legItemH = S(legItemH_u), legItemW = S(legItemW_u), legSwatchSz = S(14);
        double tableW  = nameW + numDays * cellW;
        double tableH  = colH + data.Rows.Count * rowH + colH;
        double legBoxH = legendCount > 0 ? legPadV + legTitleRowH + legTitleGap + itemRowCount * legItemH + legPadV : 0;
        double legAreaH = legendCount > 0 ? legTopGap + legBoxH : 0;
        double totalW  = tableW;
        double totalH  = titleH + tableH + legAreaH + 1;

        // ── 字體 typeface ───────────────────────────────────────────────────
        var fontFamily  = new FontFamily("Microsoft JhengHei UI, Microsoft JhengHei, sans-serif");
        var normalFace  = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal,   FontStretches.Normal);
        var boldFace    = new Typeface(fontFamily, FontStyles.Normal, FontWeights.Bold,     FontStretches.Normal);
        var semiBoldFace= new Typeface(fontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        var colorMap   = data.ShiftLegend.ToDictionary(l => l.Id, l => ParseHex(l.ColorHex));
        var legendById = data.ShiftLegend.ToDictionary(l => l.Id);

        var pen05       = FreezePen(Color.FromRgb(0xCC, 0xCC, 0xCC), 0.5);
        var pen10       = FreezePen(Color.FromRgb(0x99, 0xAA, 0xBB), 1.0);
        var outerPen    = FreezePen(Color.FromRgb(0x88, 0x99, 0xAA), 1.5);
        var colHeaderBg = FreezeColor(Color.FromRgb(0xE3, 0xEE, 0xF7));
        var nameHdrFg   = FreezeColor(Color.FromRgb(0x44, 0x66, 0x88));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));

            // ── 欄標題列（頭尾共用）──────────────────────────────────────────
            void DrawColumnHeader(double headerY)
            {
                dc.DrawRectangle(colHeaderBg, null, new Rect(0, headerY, totalW, colH));
                dc.DrawRectangle(null, pen10, new Rect(0, headerY, nameW, colH));
                var nameHdrT = Fmt("員工", semiBoldFace, S(12), nameHdrFg);
                dc.DrawText(nameHdrT, new Point((nameW - nameHdrT.Width) / 2, headerY + (colH - nameHdrT.Height) / 2));

                for (int i = startIndex; i < endIndexExclusive && i < data.Columns.Count; i++)
                {
                    var col = data.Columns[i];
                    double x = nameW + (i - startIndex) * cellW;

                    Brush colCellBg; Brush dowFg;
                    if (col.IsClosed)
                    { colCellBg = FreezeColor(Color.FromRgb(0xD5, 0xD5, 0xD5)); dowFg = Brushes.Gray; }
                    else if (col.DayOfWeekLabel == "日")
                    { colCellBg = FreezeColor(Color.FromRgb(0xFF, 0xE3, 0xE3)); dowFg = Brushes.Crimson; }
                    else if (col.DayOfWeekLabel == "六")
                    { colCellBg = FreezeColor(Color.FromRgb(0xE3, 0xEE, 0xFF)); dowFg = Brushes.RoyalBlue; }
                    else
                    { colCellBg = colHeaderBg; dowFg = FreezeColor(Color.FromRgb(0x44, 0x55, 0x66)); }

                    dc.DrawRectangle(colCellBg, null, new Rect(x, headerY, cellW, colH));
                    dc.DrawRectangle(null, pen05, new Rect(x, headerY, cellW, colH));

                    var dayT = Fmt(col.Day.ToString(), boldFace, S(14), Brushes.Black);
                    dc.DrawText(dayT, new Point(x + (cellW - dayT.Width) / 2, headerY + S(4)));

                    var dowT = Fmt(col.DayOfWeekLabel, normalFace, S(11), dowFg);
                    dc.DrawText(dowT, new Point(x + (cellW - dowT.Width) / 2,
                        headerY + colH - dowT.Height - S(5)));
                }
            }

            // ── 標題列 ──────────────────────────────────────────────────────
            dc.DrawRectangle(FreezeColor(Color.FromRgb(0x2A, 0x5C, 0x8A)), null,
                new Rect(0, 0, totalW, titleH));
            var titleT = Fmt($"{data.ShopName}　{data.Year} 年 {data.Month} 月　班表（{rangeLabel}）",
                boldFace, S(15), Brushes.White);
            dc.DrawText(titleT, new Point(S(14), (titleH - titleT.Height) / 2));

            // ── 上方欄標題列 ─────────────────────────────────────────────────
            DrawColumnHeader(titleH);

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

                for (int i = startIndex; i < endIndexExclusive && i < data.Columns.Count && i < row.ShiftIds.Count; i++)
                {
                    var col     = data.Columns[i];
                    var shiftId = row.ShiftIds[i];
                    double x    = nameW + (i - startIndex) * cellW;

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

            // ── 下方欄標題列（頭尾都有日期）──────────────────────────────────
            DrawColumnHeader(titleH + colH + data.Rows.Count * rowH);

            dc.DrawRectangle(null, outerPen, new Rect(0, titleH, totalW, tableH));

            // ── 圖例區 ───────────────────────────────────────────────────────
            if (legendCount > 0)
            {
                double legBoxY = titleH + tableH + legTopGap;

                dc.DrawRectangle(FreezeColor(Color.FromRgb(0xF1, 0xF6, 0xFB)), null,
                    new Rect(0, legBoxY, totalW, legBoxH));
                dc.DrawRectangle(null, FreezePen(Color.FromRgb(0xBB, 0xCC, 0xDD), 1.0),
                    new Rect(0, legBoxY, totalW, legBoxH));

                var legHdrT = Fmt("班別說明", semiBoldFace, S(12),
                    FreezeColor(Color.FromRgb(0x33, 0x55, 0x77)));
                dc.DrawText(legHdrT, new Point(S(12),
                    legBoxY + legPadV + (legTitleRowH - legHdrT.Height) / 2));

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
