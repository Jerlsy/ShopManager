using Microsoft.Extensions.DependencyInjection;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using ShopManager.Models;
using ShopManager.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShopManager.Views.Schedule;

/// <summary>
/// ibon 雲端列印視窗：勾選完整班表／全體個人班表 → 轉 A4 橫式 PDF → 逆向 API 上傳
/// → 取得列印碼 → 推播列印碼與 QR Code 給業主 LINE。
/// ibon 一組取件碼只能一個檔案，因此每個勾選類別各合併為一份多頁 PDF、各得一組列印碼。
/// </summary>
public partial class IbonPrintWindow : Window
{
    private readonly ExportScheduleData _data;
    private readonly BitmapSource _bitmapTop;
    private readonly BitmapSource _bitmapBottom;
    private bool _isBusy;

    public IbonPrintWindow(ExportScheduleData data, BitmapSource bitmapTop, BitmapSource bitmapBottom)
    {
        InitializeComponent();
        _data = data;
        _bitmapTop = bitmapTop;
        _bitmapBottom = bitmapBottom;
        Title = $"{data.Year} 年 {data.Month:D2} 月  ibon 雲端列印";
        Closing += (_, e) =>
        {
            if (!_isBusy) return;
            e.Cancel = true; // 上傳中不可關閉，避免上傳到一半中斷卻已產生無效列印碼
            App.Services.GetRequiredService<IAppSnackbarService>()
                .ShowWarning("上傳進行中，請等待完成後再關閉視窗");
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        bool doFull     = ChkFull.IsChecked == true;
        bool doPersonal = ChkPersonal.IsChecked == true;

        var snackbar = App.Services.GetRequiredService<IAppSnackbarService>();
        if (!doFull && !doPersonal)
        {
            snackbar.ShowWarning("請至少勾選一種班表");
            return;
        }
        if (doPersonal && _data.Rows.Count == 0)
        {
            snackbar.ShowWarning("本月沒有員工排班資料");
            return;
        }

        var owners = _data.PushRecipients.Where(r => r.IsOwner && !string.IsNullOrEmpty(r.UserId)).ToList();
        // 用 MessageBox：本視窗以 ShowDialog 開啟，MaterialDesign 的 RootDialog 掛在 MainWindow 會被壓在底下看不到
        var confirmResult = MessageBox.Show(
            $"確定要上傳所選班表？完成後列印碼與 QR Code 將推播給 {owners.Count} 位業主。",
            "上傳 ibon 雲端列印", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmResult != MessageBoxResult.Yes) return;

        _isBusy = true;
        UploadButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        ResultsPanel.Children.Clear();

        var ibon = App.Services.GetRequiredService<IbonPrintService>();
        var line = App.Services.GetRequiredService<LineService>();
        void Progress(string msg) => Dispatcher.Invoke(() => StatusText.Text = msg);

        try
        {
            var results = new List<(string Label, string Pincode, string Deadline)>();

            if (doFull)
            {
                Progress("產生完整班表 PDF…");
                var pdf = BuildPdf(new List<List<BitmapSource>>
                {
                    new() { _bitmapTop },
                    new() { _bitmapBottom },
                }, slotsPerPage: 1, centerVertically: true);
                // 不指定 selectType（單色/彩色、單雙面）：完整班表的班別色塊是彩色的，
                // 若在這裡宣告單色會跟實際內容矛盾，交給使用者在機台上自己選擇列印規格。
                var r = await ibon.UploadAsync(pdf, $"{_data.Year}{_data.Month:D2}_全體班表.pdf", Progress);
                results.Add(("完整班表", r.Pincode, r.Deadline));
            }

            if (doPersonal)
            {
                Progress("產生個人班表 PDF…");
                var pages = new List<List<BitmapSource>>();
                for (int i = 0; i < _data.Rows.Count; i += 3)
                {
                    var page = new List<BitmapSource>();
                    foreach (var row in _data.Rows.Skip(i).Take(3))
                    {
                        var rec = new ExportScheduleData.PushRecipient(
                            UserId: string.Empty, DisplayName: row.Name, PictureUrl: null,
                            IsOwner: false, ShiftIds: row.ShiftIds);
                        page.Add(ExportScheduleWindow.RenderPersonalSchedule(_data, rec));
                    }
                    pages.Add(page);
                }
                var pdf = BuildPdf(pages, slotsPerPage: 3, centerVertically: false);
                var r = await ibon.UploadAsync(pdf, $"{_data.Year}{_data.Month:D2}_個人班表.pdf", Progress);
                results.Add(("全體個人班表", r.Pincode, r.Deadline));
            }

            // ── 推播列印碼＋QR 給業主 ────────────────────────────────────────
            int notified = 0;
            foreach (var (label, pincode, deadline) in results)
            {
                Progress($"推播「{label}」列印碼給業主…");
                var text = $"【ibon 雲端列印】{_data.ShopName} {_data.Year}年{_data.Month}月 {label}\n"
                         + $"列印碼:{pincode}\n"
                         + $"期限:{deadline}\n"
                         + "請至 7-11 ibon 機台選「列印」→「雲端列印」輸入列印碼，或掃描 QR Code 列印。";

                // QR 內容即列印碼本身（與 ibon 網頁版一致），經 Worker 上傳成 LINE 可用的圖片 URL
                string? qrUrl = null, qrKey = null;
                var uploaded = await line.UploadScheduleImageAsync(
                    _data.LineWorkerUrl!, _data.LineWorkerApiKey!,
                    TaiwanPayQrService.BuildPng(pincode, pixelsPerModule: 10));
                if (uploaded is not null) (qrUrl, qrKey) = uploaded.Value;

                foreach (var owner in owners)
                {
                    bool ok = await line.PushMessageAsync(_data.LineChannelAccessToken!, owner.UserId, text);
                    if (ok && qrUrl is not null)
                        await line.PushImageAsync(_data.LineChannelAccessToken!, owner.UserId, qrUrl);
                    if (ok) notified++;
                }

                // QR 圖延後清除（給業主較充裕的開啟時間；列印碼文字不受影響）
                if (qrKey is not null)
                {
                    var key = qrKey;
                    _ = Task.Delay(TimeSpan.FromMinutes(60)).ContinueWith(_ =>
                        line.DeleteScheduleImageAsync(_data.LineWorkerUrl!, _data.LineWorkerApiKey!, key));
                }

                AddResultCard(label, pincode, deadline);
            }

            Progress("完成");
            snackbar.ShowSuccess($"已上傳 {results.Count} 份班表並推播給業主（共 {notified} 則通知）");
        }
        catch (OperationCanceledException)
        {
            snackbar.ShowWarning("已取消上傳");
        }
        catch (Exception ex)
        {
            // ibon 逆向端點改版、網路異常等都會落到這裡；提示可改走官網手動上傳
            snackbar.ShowError($"{ex.Message}／可改至 print.ibon.com.tw 手動上傳");
            Progress("上傳失敗");
        }
        finally
        {
            _isBusy = false;
            UploadButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>在結果區加入一張列印碼卡片（類別、列印碼、期限、QR）。</summary>
    private void AddResultCard(string label, string pincode, string deadline)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBox
        {
            Text = pincode,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 2, 0, 2),
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"期限：{deadline}",
            FontSize = 12,
            Foreground = (Brush)FindResource("AppSubtleTextBrush"),
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(panel);
        var qr = new Image
        {
            Source = TaiwanPayQrService.BuildBitmap(pincode, pixelsPerModule: 4),
            Width = 88, Height = 88,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(qr, 1);
        grid.Children.Add(qr);

        ResultsPanel.Children.Add(new Border
        {
            BorderBrush = (Brush)FindResource("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)FindResource("AppSurfaceBrush"),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = grid,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PDF 產生：每頁 A4 橫式，slotsPerPage 個等寬欄位由左至右排列，
    // 影像等比縮放塞入欄位（絕不裁切）；JPEG 內嵌（DCTDecode 相容性最佳）。
    // ─────────────────────────────────────────────────────────────────────────
    private static byte[] BuildPdf(List<List<BitmapSource>> pages, int slotsPerPage, bool centerVertically)
    {
        using var doc = new PdfDocument();
        var streams = new List<MemoryStream>(); // XImage 延遲讀取，須保留至 Save 完成
        try
        {
            foreach (var pageImages in pages)
            {
                var page = doc.AddPage();
                page.Size = PageSize.A4;
                page.Orientation = PageOrientation.Landscape;
                using var gfx = XGraphics.FromPdfPage(page);

                const double margin = 24, gap = 16;
                double availW = page.Width.Point - margin * 2;
                double availH = page.Height.Point - margin * 2;
                double slotW  = (availW - gap * (slotsPerPage - 1)) / slotsPerPage;

                for (int i = 0; i < pageImages.Count && i < slotsPerPage; i++)
                {
                    var ms = new MemoryStream(EncodeJpeg(pageImages[i]));
                    streams.Add(ms);
                    var img = XImage.FromStream(ms);

                    double s = Math.Min(slotW / img.PixelWidth, availH / img.PixelHeight);
                    double w = img.PixelWidth * s, h = img.PixelHeight * s;
                    double x = margin + i * (slotW + gap) + (slotW - w) / 2;
                    double y = centerVertically ? margin + (availH - h) / 2 : margin;
                    gfx.DrawImage(img, x, y, w, h);
                }
            }

            using var outMs = new MemoryStream();
            doc.Save(outMs);
            return outMs.ToArray();
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
        }
    }

    private static byte[] EncodeJpeg(BitmapSource bmp)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
