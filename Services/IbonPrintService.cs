using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ShopManager.Services;

/// <summary>
/// 7-11 ibon 雲端列印上傳服務（非官方，逆向自 print.ibon.com.tw 網頁上傳流程）。
/// 流程：GetEntry 取 JWT → GetPincode 取取件碼 → GetChunksize → Upload base64 分塊。
/// 注意：一組取件碼只能承載一個檔案（實測第一檔完成後 pincode 即封存，第二檔回 F），
/// 因此多頁內容須先合併為單一 PDF 再上傳。官方 Open API 需企業註冊用印，個人無法申請。
/// ibon 端點若改版此服務會失效，錯誤時應提示使用者改用手動上傳（print.ibon.com.tw）。
/// </summary>
public class IbonPrintService
{
    private readonly HttpClient _http;

    public IbonPrintService(HttpClient http) => _http = http;

    private const string EntryUrl    = "https://print-api.ibon.com.tw/api/BaseEntry/GetEntry";
    private const string PincodeUrl  = "https://print-api.ibon.com.tw/api/IbonUpload/GetPincode";
    private const string ChunkUrl    = "https://www.ibon.com.tw/qwsapi2/api/GetChunksize";
    private const string UploadUrl   = "https://www.ibon.com.tw/qwsapi2/api/Upload";

    private const int    SuccessCode      = 20000;
    private const int    DefaultChunkSize = 2 * 1024 * 1024;
    public  const long   MaxFileSizeBytes = 15L * 1024 * 1024;

    // 網頁版寫死的匿名識別參數（逆向自 ibon 前端；guest 上傳不需帳號）
    private const string Fv           = "4.2.2";
    private const string DisposableId = "79627-28172-49319-98742-99804.14";
    private const string KeyField     = "TnprMk1wamN0TWpneE56SXRORGt6TVRrdE9UZzNOREl0T1RrNE1EUXVNVFF0WjBwQ1ZGbz0=";
    private const string T1           = "1-5-p--gJBTZ";
    private const string Ua           = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36";

    // SelectType 代表列印規格：F + 紙張(A4/A3/4X6) + 色彩(B單色/C彩色) + 紙質(N一般/S特殊) + 單雙面(1/2)，
    // 例如 A4 單色一般紙雙面 = FA4BN2。逆向自 print.ibon.com.tw 網頁版。這裡固定用預設值，
    // 不指定色彩/單雙面，讓使用者到機台上依實際文件內容自己選——尤其班表色塊是彩色的，
    // 若程式端宣告單色會跟實際內容矛盾。
    private const string DefaultSelectType = "FNOMAL";

    /// <summary>上傳單一檔案，成功回傳（取件碼, 期限文字），失敗擲出例外（訊息可直接顯示給使用者）。</summary>
    public async Task<(string Pincode, string Deadline)> UploadAsync(
        byte[] fileBytes, string fileName, Action<string>? progress = null,
        string selectType = DefaultSelectType, CancellationToken ct = default)
    {
        if (fileBytes.Length > MaxFileSizeBytes)
            throw new InvalidOperationException($"檔案「{fileName}」超過 ibon 上限 15 MB");

        progress?.Invoke("連線 ibon 雲端…");
        var token = await FetchEntryTokenAsync(ct);

        progress?.Invoke("取得取件編號…");
        var (pincode, deadline) = await FetchPincodeAsync(token, selectType, ct);

        var chunkSize = await FetchChunkSizeAsync(ct);

        int totalChunks = (fileBytes.Length + chunkSize - 1) / chunkSize;
        var uploadTime  = DateTime.Now.ToString("yyyyMMddHHmmss") + DateTime.Now.Millisecond.ToString("D3");

        for (int serial = 1, offset = 0; offset < fileBytes.Length; serial++, offset += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Invoke(totalChunks > 1 ? $"上傳「{fileName}」（{serial}/{totalChunks}）…" : $"上傳「{fileName}」…");

            int len    = Math.Min(chunkSize, fileBytes.Length - offset);
            var buffer = Convert.ToBase64String(fileBytes, offset, len);
            var body = new
            {
                ExtParameter = new
                {
                    useMode     = "API",
                    note1       = (string?)null,
                    note2       = (string?)null,
                    note3       = (string?)null,
                    pincode,
                    fileName,
                    filesize    = fileBytes.Length,
                    isMultiFile = false,
                    fileSerial  = serial,
                    uploadTime,
                },
                buffer,
            };

            using var req = NewRequest(HttpMethod.Post, UploadUrl);
            req.Content = JsonContent.Create(body);
            var res  = await _http.SendAsync(req, ct);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var status = json.TryGetProperty("Status", out var s) ? s.GetString() : null;

            if (status == "S") return (pincode, deadline);   // 上傳完成
            if (status != "C")                               // C = 繼續下一塊
            {
                var desc = json.TryGetProperty("Description", out var d) ? d.GetString() : null;
                throw new InvalidOperationException($"ibon 上傳失敗：{desc ?? status ?? "未知錯誤"}");
            }
        }

        throw new InvalidOperationException("ibon 上傳未完成（伺服器未回報完成狀態）");
    }

    private async Task<string> FetchEntryTokenAsync(CancellationToken ct)
    {
        var body = new { Data = new { t2 = "1", fV = Fv, disposableId = DisposableId, memberToken = "", key = KeyField, t1 = T1 } };
        using var req = NewRequest(HttpMethod.Post, EntryUrl);
        req.Content = JsonContent.Create(body);
        var res  = await _http.SendAsync(req, ct);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        EnsureSuccess(json, "ibon 連線失敗");
        return json.GetProperty("result").GetProperty("token").GetString()
            ?? throw new InvalidOperationException("ibon 連線失敗：未取得授權");
    }

    private async Task<(string Pincode, string Deadline)> FetchPincodeAsync(string token, string selectType, CancellationToken ct)
    {
        var body = new { Data = new { User = "guest", Email = "guest@qware.com.tw", SelectType = selectType } };
        using var req = NewRequest(HttpMethod.Post, PincodeUrl);
        req.Headers.TryAddWithoutValidation("Authorization", token);
        req.Headers.TryAddWithoutValidation("key", DisposableId);
        req.Headers.TryAddWithoutValidation("fv", Fv);
        req.Content = JsonContent.Create(body);
        var res  = await _http.SendAsync(req, ct);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        EnsureSuccess(json, "ibon 取件編號取得失敗");
        var result  = json.GetProperty("result");
        var pincode = result.GetProperty("pincode").GetString();
        var deadline = result.TryGetProperty("deadLine", out var dl) ? dl.GetString() : null;
        if (string.IsNullOrEmpty(pincode))
            throw new InvalidOperationException("ibon 取件編號取得失敗：回應無 pincode");
        return (pincode, deadline ?? "上傳後 3 天內");
    }

    private async Task<int> FetchChunkSizeAsync(CancellationToken ct)
    {
        try
        {
            using var req = NewRequest(HttpMethod.Get, ChunkUrl);
            var res  = await _http.SendAsync(req, ct);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return json.TryGetProperty("ChunkSize", out var c) && c.TryGetInt32(out var size) && size > 0
                ? size : DefaultChunkSize;
        }
        catch
        {
            return DefaultChunkSize; // 拿不到就用網頁版預設 2 MiB
        }
    }

    private static void EnsureSuccess(JsonElement json, string what)
    {
        var code = json.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
        if (code != SuccessCode)
        {
            var msg = json.TryGetProperty("msg", out var m) ? m.GetString() : null;
            throw new InvalidOperationException($"{what}（code={code}）{msg}");
        }
    }

    /// <summary>模擬 ibon 網頁前端的請求標頭（缺 Origin/Referer 會被擋）。</summary>
    private static HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("User-Agent", Ua);
        req.Headers.TryAddWithoutValidation("Origin", "https://print.ibon.com.tw");
        req.Headers.TryAddWithoutValidation("Referer", "https://print.ibon.com.tw/");
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        req.Headers.TryAddWithoutValidation("Accept-Language", "zh-TW,zh;q=0.9,en-US;q=0.8");
        return req;
    }
}
