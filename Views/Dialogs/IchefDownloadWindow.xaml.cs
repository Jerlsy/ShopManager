using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;
using Microsoft.Web.WebView2.Core;
using ShopManager.Data;
using ShopManager.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShopManager.Views.Dialogs;

public enum IchefDownloadStatus
{
    Success,
    Cancelled,
    /// <summary>登入或下載方式失敗（含 iCHEF 改版）：呼叫端改請使用者手動選檔</summary>
    Failed,
    /// <summary>下載成功但該月沒有打卡紀錄：不需要再叫使用者選檔</summary>
    Empty,
}

public record IchefDownloadResult(IchefDownloadStatus Status, byte[]? Content = null, string? Message = null);

public enum IchefStepState { Pending, Active, Done, Failed }

public partial class IchefStep : ObservableObject
{
    public string Title { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    private IchefStepState _state;

    [ObservableProperty] private string _detail = string.Empty;

    public PackIconKind Icon => State switch
    {
        IchefStepState.Done   => PackIconKind.CheckCircle,
        IchefStepState.Active => PackIconKind.ProgressClock,
        IchefStepState.Failed => PackIconKind.AlertCircle,
        _                     => PackIconKind.CircleOutline,
    };

    public Brush IconBrush => Application.Current.TryFindResource(State switch
    {
        IchefStepState.Done   => "PrimaryHueMidBrush",
        IchefStepState.Active => "PrimaryHueMidBrush",
        IchefStepState.Failed => "MaterialDesign.Brush.ValidationError",
        _                     => "AppSubtleForegroundBrush",
    }) as Brush ?? Brushes.Gray;
}

/// <summary>
/// 從 iCHEF 後台自動下載指定月份的打卡紀錄 xlsx。
/// 內嵌瀏覽器只負責讓使用者登入（帳密與登入狀態由 WebView2 自己保管，程式不存密碼）；
/// 登入後直接用該登入狀態請求後台「下載打卡記錄」按鈕背後的網址，不模擬點擊。
/// 該網址是 iCHEF 沒公開的介面，改版就可能失效 → 任何無法完成的情況都回報 Failed，由呼叫端改走手動選檔。
/// </summary>
public partial class IchefDownloadWindow : Window
{
    private const string StartUrl = "https://login.ichefpos.com/analyse/index/clockin_log_report";

    private readonly Guid _shopId;
    private readonly int _year;
    private readonly int _month;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private bool _probing;
    private bool _finished;
    private int _probeErrors;

    public ObservableCollection<IchefStep> Steps { get; }
    public IchefDownloadResult Result { get; private set; } = new(IchefDownloadStatus.Cancelled);

    public IchefDownloadWindow(Guid shopId, int year, int month)
    {
        _shopId = shopId;
        _year   = year;
        _month  = month;

        Steps = new ObservableCollection<IchefStep>
        {
            new() { Title = "開啟 iCHEF 後台" },
            new() { Title = "登入帳號" },
            new() { Title = $"下載 {year} 年 {month} 月打卡資料" },
            new() { Title = "檢查檔案內容" },
        };

        InitializeComponent();
        DataContext = this;

        HeaderText.Text = $"正在取得 {year} 年 {month} 月的打卡資料";
        SubText.Text    = "登入後會自動下載，不需要再按任何按鈕";
        BannerText.Text = "請在下方登入 iCHEF 後台（輸入 Store ID、帳號、密碼）。登入完成後此視窗會自動繼續。";
        Steps[0].State  = IchefStepState.Active;
        StatusText.Text = "正在啟動內嵌瀏覽器…";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 每家店各自一份瀏覽器資料夾：不同店可能用不同的 iCHEF 帳號，登入狀態不能混在一起
            var folder = Path.Combine(
                Path.GetDirectoryName(AppDbContext.DefaultDbPath())!,
                "WebView2-iCHEF", _shopId.ToString("N"));
            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await Browser.EnsureCoreWebView2Async(env);

            var settings = Browser.CoreWebView2.Settings;
            settings.IsPasswordAutosaveEnabled = true;   // 讓瀏覽器自己記住帳密，下次自動填入
            settings.IsGeneralAutofillEnabled  = true;
            settings.AreDevToolsEnabled        = false;

            Browser.NavigationCompleted += (_, _) =>
            {
                if (Steps[0].State != IchefStepState.Active) return;
                Steps[0].State  = IchefStepState.Done;
                Steps[1].State  = IchefStepState.Active;
                Steps[1].Detail = "（等待你登入）";
                StatusText.Text = "等待登入…若之前登入過，會自動通過。";
            };
            Browser.CoreWebView2.Navigate(StartUrl);

            // 不靠頁面事件判斷「已登入」（登入可能是前端換頁、事件不一定觸發）：
            // 每隔一小段時間試著請求下載網址，成功或確定在登入後的頁面就繼續
            _timer.Tick += async (_, _) => await ProbeAsync();
            _timer.Start();
        }
        catch (Exception ex)
        {
            Fail($"無法啟動內嵌瀏覽器：{ex.Message}");
        }
    }

    private async Task ProbeAsync()
    {
        if (_probing || _finished || Steps[0].State != IchefStepState.Done) return;
        _probing = true;
        try
        {
            var raw = await Browser.CoreWebView2.ExecuteScriptAsync(BuildScript());
            var json = JsonSerializer.Deserialize<string>(raw);
            _probeErrors = 0;
            if (json is null) return;

            using var doc = JsonDocument.Parse(json);
            var root   = doc.RootElement;
            var status = root.GetProperty("s").GetString();
            var path   = root.GetProperty("p").GetString() ?? "/";
            bool onLoginPage = path == "/" || path.Contains("login", StringComparison.OrdinalIgnoreCase);

            if (status == "ok")
            {
                EnterBackgroundMode();
                await HandleDownloadedAsync(Convert.FromBase64String(root.GetProperty("d").GetString() ?? ""));
            }
            else if (status != "login" && !onLoginPage)
            {
                // 已經在登入後的頁面，下載網址卻不是 xlsx（404／改成別的回應）→ 後台改版了
                var http = root.TryGetProperty("c", out var c) ? c.GetInt32() : 0;
                EnterBackgroundMode();
                Fail($"iCHEF 後台的下載方式似乎已變更（HTTP {http}）");
            }
        }
        catch (Exception ex)
        {
            // 換頁途中腳本執行失敗是正常的：下一輪再試；但連續失敗太久就不是換頁了，直接回報（改走手動選檔）
            if (++_probeErrors >= 40) Fail($"無法偵測登入狀態：{ex.Message}");
        }
        finally { _probing = false; }
    }

    /// <summary>登入完成：收起瀏覽器，只留進度</summary>
    private void EnterBackgroundMode()
    {
        if (Steps[1].State == IchefStepState.Done) return;

        Steps[1].State  = IchefStepState.Done;
        Steps[1].Detail = "（已登入）";
        Steps[2].State  = IchefStepState.Active;
        StatusText.Text = "已登入，正在下載打卡資料…";

        Browser.Visibility      = Visibility.Collapsed;
        LoginBanner.Visibility  = Visibility.Collapsed;
        SizeToContent           = SizeToContent.Height;
        if (Owner is not null)
        {
            Left = Owner.Left + (Owner.ActualWidth - Width) / 2;
            Top  = Owner.Top + 120;
        }
    }

    private async Task HandleDownloadedAsync(byte[] content)
    {
        _timer.Stop();
        Steps[2].State  = IchefStepState.Done;
        Steps[2].Detail = $"（{content.Length / 1024.0:0.#} KB）";
        Steps[3].State  = IchefStepState.Active;
        StatusText.Text = "正在檢查檔案內容…";

        int punchCount;
        try
        {
            punchCount = await Task.Run(() => AttendanceImportService.Parse(content)
                .Sum(p => p.Punches.Count(x => x.Time.Year == _year && x.Time.Month == _month)));
        }
        catch (Exception ex)
        {
            Fail($"下載的檔案格式無法辨識（iCHEF 可能已改版）：{ex.Message}");
            return;
        }

        if (punchCount == 0)
        {
            Steps[3].State = IchefStepState.Failed;
            Finish(new IchefDownloadResult(IchefDownloadStatus.Empty,
                Message: $"iCHEF 後台沒有 {_year} 年 {_month} 月的打卡紀錄"));
            return;
        }

        Steps[3].State  = IchefStepState.Done;
        Steps[3].Detail = $"（{punchCount} 筆打卡）";
        Progress.IsIndeterminate = false;
        Progress.Value  = 100;
        HeaderText.Text = "下載完成";
        StatusText.Text = "即將回到薪資計算設定…";
        Finish(new IchefDownloadResult(IchefDownloadStatus.Success, content), delayMs: 700);
    }

    private void Fail(string message)
    {
        var active = Steps.FirstOrDefault(s => s.State == IchefStepState.Active);
        if (active is not null) active.State = IchefStepState.Failed;
        HeaderText.Text = "無法自動下載";
        Progress.IsIndeterminate = false;
        StatusText.Text = message + "\n即將改為讓你手動選擇打卡檔案。";
        Finish(new IchefDownloadResult(IchefDownloadStatus.Failed, Message: message), delayMs: 2200);
    }

    private async void Finish(IchefDownloadResult result, int delayMs = 1500)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        Result = result;
        CancelButton.IsEnabled = false;
        await Task.Delay(delayMs);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e) => _timer.Stop();

    private void Window_Closed(object? sender, EventArgs e) => Browser.Dispose();

    /// <summary>
    /// 在後台頁面內請求「下載打卡記錄」背後的網址（參數與後台頁面自己送出的相同：整月、全天、全部星期／客數／用餐方式）。
    /// ExecuteScriptAsync 不會等 Promise 完成（只回傳空物件），所以腳本分兩輪：
    /// 第一輪啟動請求並回傳 null，結果存在頁面的 window.__ip；之後每輪取回結果（取到後清掉，下一輪重新請求）。
    /// 結果 JSON：s＝ok（拿到 xlsx，d＝base64）／login（尚未登入）／changed（其他）／error；p＝目前頁面路徑，c＝HTTP 狀態。
    /// </summary>
    private string BuildScript()
    {
        var first = new DateOnly(_year, _month, 1);
        var last  = first.AddMonths(1).AddDays(-1);
        const string template = """
            (function () {
              if (window.__ip) {
                const r = window.__ip.r;
                if (r) { window.__ip = null; return r; }
                return null;
              }
              window.__ip = { r: null };
              const slot = window.__ip;
              (async () => {
                const p = location.pathname;
                try {
                  const q = new URLSearchParams({
                    start_date: '__START__', end_date: '__END__', start_hour: '0', end_hour: '24',
                    weekday: '1,1,1,1,1,1,1', customer_no: '1,99', service_types: 'indoor,takeout,delivery'
                  });
                  const r = await fetch('/analyse/api/clock_in_download/?' + q.toString(), { credentials: 'include' });
                  const ct = (r.headers.get('content-type') || '').toLowerCase();
                  if (r.ok && ct.includes('spreadsheetml')) {
                    const b = new Uint8Array(await r.arrayBuffer());
                    let s = '';
                    for (let i = 0; i < b.length; i += 0x8000) s += String.fromCharCode.apply(null, b.subarray(i, i + 0x8000));
                    slot.r = JSON.stringify({ s: 'ok', p: p, c: r.status, d: btoa(s) });
                    return;
                  }
                  const u = new URL(r.url);
                  if (r.status === 401 || r.status === 403 || u.pathname === '/' || /login/i.test(u.pathname))
                    slot.r = JSON.stringify({ s: 'login', p: p, c: r.status });
                  else
                    slot.r = JSON.stringify({ s: 'changed', p: p, c: r.status });
                } catch (e) {
                  slot.r = JSON.stringify({ s: 'error', p: p, c: 0 });
                }
              })();
              return null;
            })()
            """;
        return template
            .Replace("__START__", first.ToString("yyyy-MM-dd"))
            .Replace("__END__", last.ToString("yyyy-MM-dd"));
    }
}
