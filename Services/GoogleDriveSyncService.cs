using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using ShopManager.Data;
using ShopManager.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace ShopManager.Services;

/// <summary>一條要上傳給 Apps Script 的Gmail轉Line推播規則</summary>
public record GmailForwardRuleUpload(
    string Id, string? SenderContains, string? SubjectContains,
    IReadOnlyList<GmailForwardOwner> Owners, IReadOnlyList<string> ContentFields);

/// <summary>推播對象（LINE userId ＋ 顯示名稱，名稱用於訊息開頭的稱謂）</summary>
public record GmailForwardOwner(string UserId, string Name);

/// <summary>測試Gmail轉Line推播規則時，Gmail 上最新一封符合條件的信件摘要</summary>
public record GmailRulePreview(string From, string Subject, string Snippet);

/// <summary>單一服務（標記／推播）的健康狀態，依 Apps Script 回報的心跳判斷</summary>
public enum GmailServiceHealth
{
    /// <summary>從沒執行過（狀態檔不存在，或檔案裡沒有這個服務的紀錄）</summary>
    NotDeployed,
    /// <summary>曾經執行過，但最後一次執行距今超過 1 天，疑似觸發器停止或帳號授權失效</summary>
    Stale,
    /// <summary>有在執行，但最後一次執行失敗</summary>
    Error,
    /// <summary>正常</summary>
    Healthy,
}

/// <summary>Gmail轉Line推播機制的部署狀態（規則檔＋Apps Script 心跳）</summary>
public record GmailForwardDeployStatus(
    bool ConfigUploaded, string? ConfigFileId,
    GmailServiceHealth Health, DateTimeOffset? LastRunAt, string? LastError,
    int PushedCountLastRun, int GivenUpCountLastRun);

/// <summary>
/// Google Drive 資料備份／還原服務。
///
/// 設計要點：
/// • 這個 App 所有店鋪共用同一個 SQLite 檔案，但每個店鋪各自綁定不同的 Google 帳號，所以備份／
///   還原的範圍是「這個店鋪自己的資料」（見 <see cref="ShopDataPortabilityService"/>），不是整個
///   資料庫檔案——否則一個店鋪的備份會夾帶其他店鋪的資料，還原時也會覆蓋到其他店鋪。
/// • 備份檔存在 Drive 的 <c>appDataFolder</c> 特殊空間：使用者在雲端硬碟網頁看不到、不會誤分享，
///   只有本 App 的授權能存取。搭配 <c>drive.appdata</c> 範圍——這是最窄的範圍，碰不到使用者其他檔案。
/// • OAuth refresh token 用 Windows DPAPI 加密存在本機（見 <see cref="DpapiDataStore"/>），不進 DB。
///   DPAPI 綁定「本機＋本 Windows 使用者」，跟著 DB 同步到別台機器也解不開，故換機器須各自授權一次。
/// • 同步一律由使用者主動觸發、單向覆蓋，不做自動合併，也不搶鎖。
/// </summary>
public class GoogleDriveSyncService(ShopContext shopContext, ShopDataPortabilityService portability)
{
    // ── OAuth 用戶端憑證 ─────────────────────────────────────────────────────
    // 桌面應用程式（Desktop app）類型的 client secret 依 Google 官方設計並非機密：
    // 它必然隨程式散布、任何人都能從執行檔取出，安全性靠的是使用者本人的授權同意。
    // 若需撤銷或更換，到 Google Cloud Console →「憑證」重新產生後替換此處即可。
    private const string ClientId     = "634899319959-1jt9cnrqmrq4rbsiasu9uuk4gqc5p4mu.apps.googleusercontent.com";
    private const string ClientSecret = "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";

    private const string AppName       = "ShopManager";
    private const string UserKey       = "user";          // DataStore 內的鍵名（單一使用者）
    private const string AppDataFolder = "appDataFolder"; // Drive 隱藏空間的保留字

    // 雲端檔名一律帶 ShopId：appDataFolder 是「依 Google 帳號 × 應用程式」隔離的，不分店鋪。
    // 若使用者把同一個 Google 帳號綁到兩個店鋪（設定上沒有東西阻止），固定檔名會讓兩店互相覆蓋
    // ——B 店備份蓋掉 A 店的檔案，A 店還原就拿到 B 店的資料。帶上 ShopId 才真正各自獨立。
    private string BackupFileName     => $"shopmanager-backup-{shopContext.ShopId}.json";
    private string MailRulesFileName  => $"shopmanager-mail-rules-{shopContext.ShopId}.json";
    private string MailStatusFileName => $"shopmanager-mail-status-{shopContext.ShopId}.json";

    // appDataFolder 只給備份用（該空間依應用程式隔離，Apps Script 讀不到）；
    // 郵件規則／心跳檔要放一般 Drive 空間才能讓 Apps Script 用同一個 Google 帳號讀到，
    // 所以額外要 drive.file（只能存取本 App 自己建立的檔案）跟 gmail.readonly（規則測試用）。
    private static readonly string[] Scopes =
    [
        DriveService.ScopeConstants.DriveAppdata,
        "https://www.googleapis.com/auth/drive.file",
        "https://www.googleapis.com/auth/gmail.readonly",
    ];

    /// <summary>還原前，目前店鋪資料的備份（只保留一份，每次還原直接覆蓋）</summary>
    public static string PreRestoreBackupPath => Path.Combine(
        Path.GetDirectoryName(AppDbContext.OverrideDbPath ?? AppDbContext.DefaultDbPath())!,
        "shopmanager_before_restore.json");

    private static string LiveDbPath => AppDbContext.OverrideDbPath ?? AppDbContext.DefaultDbPath();

    // 憑證檔按店鋪分開：各店鋪可綁不同 Google 帳號（與 ShopSetting 內的綁定設定一致）
    private string CredentialPath => Path.Combine(
        Path.GetDirectoryName(LiveDbPath)!,
        $"google-credential-{shopContext.ShopId}.dat");

    private GoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
        Scopes        = Scopes,
        DataStore     = new DpapiDataStore(CredentialPath),
    });

    /// <summary>本機是否已存有這個店鋪的授權憑證（不連網、不開瀏覽器）</summary>
    public bool HasLocalCredential() => File.Exists(CredentialPath);

    // ─────────────────────────────────────────────────────────────────────────
    // 授權
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>開啟瀏覽器讓使用者登入並同意授權，成功後回傳帳號 Email</summary>
    public async Task<string> LinkAccountAsync(
        Action<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Invoke("等待瀏覽器完成 Google 授權…");
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
            Scopes, UserKey, ct, new DpapiDataStore(CredentialPath));

        progress?.Invoke("讀取帳號資訊…");
        using var drive = CreateDriveService(credential);

        var aboutReq = drive.About.Get();
        aboutReq.Fields = "user(emailAddress)";
        var about = await aboutReq.ExecuteAsync(ct);
        return about.User?.EmailAddress ?? "（未知帳號）";
    }

    /// <summary>解除連結：刪除本機憑證檔（不會刪除雲端上的備份檔）</summary>
    public void Unlink()
    {
        if (File.Exists(CredentialPath)) File.Delete(CredentialPath);
    }

    /// <summary>
    /// 用本機已存的憑證靜默取得授權（不開瀏覽器）。沒有憑證、或憑證已被撤銷時回傳 null。
    /// </summary>
    private async Task<UserCredential?> TryGetCredentialAsync(CancellationToken ct)
    {
        if (!HasLocalCredential()) return null;
        try
        {
            var flow  = CreateFlow();
            var token = await flow.LoadTokenAsync(UserKey, ct);
            if (token is null) return null;
            // access token 過期時，UserCredential 會在實際呼叫 API 時用 refresh token 自動換新；
            // 若 refresh token 已被使用者撤銷，會在呼叫處拋 TokenResponseException，由各方法接手。
            return new UserCredential(flow, UserKey, token);
        }
        catch
        {
            return null; // 憑證檔損毀／格式不符（例如換過 Windows 使用者導致 DPAPI 解不開）
        }
    }

    private static DriveService CreateDriveService(UserCredential credential) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName       = AppName,
    });

    // ─────────────────────────────────────────────────────────────────────────
    // 查詢雲端狀態
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 取得雲端備份檔的最後修改時間。回傳 null 表示未授權、雲端沒有備份、或查詢失敗（離線等）。
    /// 呼叫端據此與 <c>ShopSetting.GoogleDriveLastSyncedRemoteModifiedTime</c> 比較判斷雲端是否較新。
    /// </summary>
    public async Task<DateTimeOffset?> GetRemoteStatusAsync(CancellationToken ct = default)
    {
        var credential = await TryGetCredentialAsync(ct);
        if (credential is null) return null;

        try
        {
            using var drive = CreateDriveService(credential);
            var found = await FindBackupAsync(drive, ct);
            return found?.ModifiedTimeDateTimeOffset;
        }
        catch
        {
            // 離線、授權被撤銷、Drive 服務異常等一律視為「查不到」，不打斷流程
            return null;
        }
    }

    /// <summary>在 appDataFolder 內按檔名找出這個店鋪的備份檔（不存在回傳 null）</summary>
    private async Task<DriveFile?> FindBackupAsync(DriveService drive, CancellationToken ct)
    {
        var list = drive.Files.List();
        list.Spaces   = AppDataFolder;
        list.Q        = $"name = '{BackupFileName}' and trashed = false";
        list.Fields   = "files(id, modifiedTime)";
        list.PageSize = 1;
        var result = await list.ExecuteAsync(ct);
        return result.Files?.FirstOrDefault();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 備份：本機 → 雲端
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 匯出這個店鋪自己的資料成 JSON 並上傳覆蓋雲端備份。雲端已有備份時更新同一個檔案（不累積多份）。
    /// 回傳上傳後雲端檔案的 modifiedTime，呼叫端要記進 ShopSetting 當作「已同步到這個版本」。
    /// </summary>
    public async Task<DateTimeOffset> BackupAsync(
        Action<string>? progress = null, CancellationToken ct = default)
    {
        var credential = await TryGetCredentialAsync(ct)
            ?? throw new InvalidOperationException("尚未連結 Google 帳號，或授權已失效，請重新連結。");

        progress?.Invoke("匯出店鋪資料…");
        var json = await portability.ExportAsync(shopContext.ShopId, ct);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var drive = CreateDriveService(credential);
        var existing = await FindBackupAsync(drive, ct);

        using var content = new MemoryStream(bytes);
        var sizeMb = bytes.Length / 1024.0 / 1024.0;

        DriveFile uploaded;
        if (existing is null)
        {
            progress?.Invoke($"上傳備份（{sizeMb:F1} MB）…");
            var metadata = new DriveFile
            {
                Name    = BackupFileName,
                Parents = [AppDataFolder],
            };
            var req = drive.Files.Create(metadata, content, "application/json");
            req.Fields = "id, modifiedTime";
            var upload = await req.UploadAsync(ct);
            ThrowIfUploadFailed(upload);
            uploaded = req.ResponseBody;
        }
        else
        {
            progress?.Invoke($"更新雲端備份（{sizeMb:F1} MB）…");
            // 更新既有檔案時 metadata 不可帶 Parents（Drive 會拒絕）
            var req = drive.Files.Update(new DriveFile(), existing.Id, content, "application/json");
            req.Fields = "id, modifiedTime";
            var upload = await req.UploadAsync(ct);
            ThrowIfUploadFailed(upload);
            uploaded = req.ResponseBody;
        }

        var modified = uploaded?.ModifiedTimeDateTimeOffset
            ?? throw new InvalidOperationException("上傳完成但未取得雲端檔案時間，請重試。");

        progress?.Invoke("備份完成");
        return modified;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 還原：雲端 → 本機（只覆蓋這個店鋪自己的資料，其他店鋪不受影響）
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 下載雲端備份，清空並重新寫入這個店鋪自己的資料。覆蓋前會先把目前的店鋪資料另存一份
    /// （只保留一份，供出錯時手動回溯）。完成後呼叫端必須重啟程式：頁面與 ViewModel 仍持有舊資料，
    /// 熱替換會讀到不一致的狀態。
    /// </summary>
    public async Task RestoreAsync(Action<string>? progress = null, CancellationToken ct = default)
    {
        var credential = await TryGetCredentialAsync(ct)
            ?? throw new InvalidOperationException("尚未連結 Google 帳號，或授權已失效，請重新連結。");

        using var drive = CreateDriveService(credential);

        progress?.Invoke("尋找雲端備份…");
        var remote = await FindBackupAsync(drive, ct)
            ?? throw new InvalidOperationException("雲端上找不到備份檔，請先在其他電腦執行一次備份。");

        progress?.Invoke("下載雲端備份…");
        using var downloaded = new MemoryStream();
        await drive.Files.Get(remote.Id).DownloadAsync(downloaded, ct);
        var json = Encoding.UTF8.GetString(downloaded.ToArray());

        progress?.Invoke("備份目前店鋪資料…");
        var shopId = shopContext.ShopId;
        var localBackupJson = await portability.ExportAsync(shopId, ct);
        await File.WriteAllTextAsync(PreRestoreBackupPath, localBackupJson, ct);

        progress?.Invoke("寫入還原資料…");
        // ImportAsync 會先反序列化整份 JSON、驗證格式，格式不對會直接拋例外，
        // 這個時候還沒開始刪除任何資料，本機資料不會受影響。
        //
        // 一併把雲端檔案的 modifiedTime 記進還原出來的 ShopSetting：備份 JSON 是「上傳之前」
        // 匯出的，裡面的同步時間必然比它自己在雲端的 modifiedTime 舊，不覆蓋掉的話設定頁
        // 每次都會判定「雲端比較新」而重複跳出還原提示。
        await portability.ImportAsync(shopId, json, remote.ModifiedTimeDateTimeOffset, ct);

        progress?.Invoke("還原完成，準備重新啟動…");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gmail轉Line推播：規則 JSON 上傳／部署狀態偵測／規則測試
    //
    // 規則檔與心跳檔都放在一般 Drive 空間（不是 appDataFolder），因為 Apps Script
    // 是用同一個 Google 帳號自己的身分執行，appDataFolder 依應用程式隔離、Apps Script
    // 讀不到 ShopManager 寫進去的東西；一般空間搭配 drive.file 範圍才能兩邊互通。
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>在一般 Drive 空間（非 appDataFolder）按檔名找檔案</summary>
    private static async Task<DriveFile?> FindFileInDriveAsync(DriveService drive, string fileName, CancellationToken ct)
    {
        var list = drive.Files.List();
        list.Q        = $"name = '{fileName}' and trashed = false";
        list.Fields   = "files(id, modifiedTime)";
        list.PageSize = 1;
        var result = await list.ExecuteAsync(ct);
        return result.Files?.FirstOrDefault();
    }

    /// <summary>把 JSON 內容寫進一般 Drive 空間的指定檔名（已存在就覆蓋），回傳檔案 Id</summary>
    private static async Task<string> UpsertDriveFileAsync(
        DriveService drive, string fileName, string json, CancellationToken ct)
    {
        var existing = await FindFileInDriveAsync(drive, fileName, ct);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(json));

        if (existing is null)
        {
            var req = drive.Files.Create(new DriveFile { Name = fileName }, content, "application/json");
            req.Fields = "id";
            ThrowIfUploadFailed(await req.UploadAsync(ct));
            return req.ResponseBody?.Id
                ?? throw new InvalidOperationException("上傳完成但未取得雲端檔案 Id，請重試。");
        }

        var updateReq = drive.Files.Update(new DriveFile(), existing.Id, content, "application/json");
        updateReq.Fields = "id";
        ThrowIfUploadFailed(await updateReq.UploadAsync(ct));
        return existing.Id;
    }

    /// <summary>
    /// 上傳Gmail轉Line推播規則（含 LINE token）供 Apps Script 讀取。
    /// 部署指南需要的規則檔 File Id 由 <see cref="GetDeployStatusAsync"/> 回報，這裡不重複回傳。
    ///
    /// 這裡會順便把心跳檔先建出來（內容 <c>{}</c>），並把它的 Id 寫進規則 JSON 讓 Apps Script 直接寫入。
    /// 這一步是必要的而不是圖方便：ShopManager 只有 <c>drive.file</c> 範圍，看得到的僅限「本 App 自己
    /// 建立的檔案」。若心跳檔是 Apps Script 那邊自己 create 出來的，擁有者雖然同樣是使用者，
    /// 但對 ShopManager 而言是外部檔案、查詢不到，部署狀態就會永遠顯示「尚未部署」。
    /// </summary>
    public async Task UploadMailRulesAsync(
        IReadOnlyList<GmailForwardRuleUpload> rules, string lineChannelAccessToken, CancellationToken ct = default)
    {
        var credential = await TryGetCredentialAsync(ct)
            ?? throw new InvalidOperationException("尚未連結 Google 帳號，或授權已失效，請重新連結。");

        using var drive = CreateDriveService(credential);

        // 心跳檔只在不存在時建立，已存在就沿用（不要覆蓋掉 Apps Script 已回報的執行紀錄）
        var statusFile = await FindFileInDriveAsync(drive, MailStatusFileName, ct);
        var statusFileId = statusFile?.Id
            ?? await UpsertDriveFileAsync(drive, MailStatusFileName, "{}", ct);

        var payload = new
        {
            updatedAt = DateTimeOffset.Now,
            statusFileId,
            lineChannelAccessToken,
            rules = rules.Select(r => new
            {
                id = r.Id,
                senderContains = r.SenderContains,
                subjectContains = r.SubjectContains,
                owners = r.Owners.Select(o => new { userId = o.UserId, name = o.Name }),
                contentFields = r.ContentFields,
            }),
        };

        await UpsertDriveFileAsync(drive, MailRulesFileName, JsonSerializer.Serialize(payload), ct);
    }

    /// <summary>
    /// 偵測Gmail轉Line推播機制的部署狀態：規則檔存不存在、Apps Script 的兩個服務心跳是否正常。
    /// 心跳檔（<see cref="MailStatusFileName"/>）由 Apps Script 每次執行後回寫，這裡只負責讀取判讀，
    /// 不負責建立——沒有心跳檔就代表 Apps Script 從沒被執行過。
    /// </summary>
    public async Task<GmailForwardDeployStatus> GetDeployStatusAsync(CancellationToken ct = default)
    {
        var credential = await TryGetCredentialAsync(ct)
            ?? throw new InvalidOperationException("尚未連結 Google 帳號，或授權已失效，請重新連結。");

        using var drive = CreateDriveService(credential);

        var rulesFile = await FindFileInDriveAsync(drive, MailRulesFileName, ct);
        if (rulesFile is null)
            return new GmailForwardDeployStatus(false, null, GmailServiceHealth.NotDeployed, null, null, 0, 0);

        var statusFile = await FindFileInDriveAsync(drive, MailStatusFileName, ct);
        if (statusFile is null)
            return new GmailForwardDeployStatus(true, rulesFile.Id, GmailServiceHealth.NotDeployed, null, null, 0, 0);

        using var stream = new MemoryStream();
        await drive.Files.Get(statusFile.Id).DownloadAsync(stream, ct);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var payload = JsonSerializer.Deserialize<MailStatusPayload>(stream.ToArray(), options) ?? new MailStatusPayload();

        return new GmailForwardDeployStatus(true, rulesFile.Id,
            EvaluateHealth(payload.LastRunAt, payload.LastRunOk),
            payload.LastRunAt, payload.LastError,
            payload.PushedCountLastRun, payload.GivenUpCountLastRun);
    }

    private static GmailServiceHealth EvaluateHealth(DateTimeOffset? lastRunAt, bool lastRunOk)
    {
        if (lastRunAt is null) return GmailServiceHealth.NotDeployed;
        if (!lastRunOk) return GmailServiceHealth.Error;
        if (DateTimeOffset.Now - lastRunAt.Value > TimeSpan.FromDays(1)) return GmailServiceHealth.Stale;
        return GmailServiceHealth.Healthy;
    }

    private class MailStatusPayload
    {
        public DateTimeOffset? LastRunAt { get; set; }
        public bool LastRunOk { get; set; } = true;
        public string? LastError { get; set; }

        /// <summary>上一輪成功推播的封數</summary>
        public int PushedCountLastRun { get; set; }

        /// <summary>上一輪因連續失敗達上限而放棄的封數</summary>
        public int GivenUpCountLastRun { get; set; }
    }

    /// <summary>
    /// 實際推播只處理近幾天內的信件（見 Apps Script 樣板的 RECENT_DAYS），避免第一次部署或
    /// 新增規則時把整個信箱的歷史信件一次推光。這裡跟著設同一個值：測試找到的信如果超出這個
    /// 範圍，會在結果裡加註提醒——不然使用者會看到測試成功卻永遠等不到這封信被推播的落差。
    /// </summary>
    private const int RecentDaysWindow = 7;

    /// <summary>
    /// 用寄件者／主旨條件直接查 Gmail，回傳最新一封符合信件的寄件者、主旨，以及
    /// 「實際會推播出去的內容」——正文會經過與 Apps Script 相同的清理／關鍵字擷取，
    /// 使用者才能靠測試結果調整關鍵字。查無符合信件回傳 null（不算失敗）。
    /// </summary>
    public async Task<GmailRulePreview?> TestGmailRuleAsync(
        string? senderContains, string? subjectContains,
        IReadOnlyList<string>? contentFields = null, CancellationToken ct = default)
    {
        var credential = await TryGetCredentialAsync(ct)
            ?? throw new InvalidOperationException("尚未連結 Google 帳號，或授權已失效，請重新連結。");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(senderContains))  parts.Add($"from:({senderContains})");
        if (!string.IsNullOrWhiteSpace(subjectContains)) parts.Add($"subject:({subjectContains})");
        if (parts.Count == 0)
            throw new InvalidOperationException("請至少輸入寄件者或主旨其中一項條件。");

        using var gmail = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = AppName,
        });

        var listReq = gmail.Users.Messages.List("me");
        listReq.Q = string.Join(" ", parts);
        listReq.MaxResults = 1;
        var listRes = await listReq.ExecuteAsync(ct);
        var first = listRes.Messages?.FirstOrDefault();
        if (first is null) return null;

        // 要 Full 才拿得到正文；Metadata 只有標頭與 snippet，看不出擷取結果對不對
        var getReq = gmail.Users.Messages.Get("me", first.Id);
        getReq.Format = Google.Apis.Gmail.v1.UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var msg = await getReq.ExecuteAsync(ct);

        string? Header(string name) => msg.Payload?.Headers?.FirstOrDefault(h => h.Name == name)?.Value;

        // 優先自己轉 HTML，而不是用現成的 text/plain：郵件內建的純文字版是由寄件方或 Gmail 產生的，
        // 不會理會 CSS，銀行拿 font-size:0 藏的隱形字元會原封不動混進值裡（存入帳號前面那個「/」就是）。
        // 自己轉才能把看不見的元素一起丟掉。沒有 HTML 段落時才退回 text/plain。
        var body = (FindPart(msg.Payload, "text/html") is { } html ? MailBodyExtractor.HtmlToText(html) : null)
                   ?? FindPart(msg.Payload, "text/plain");

        string content;
        if (body is null)
        {
            content = msg.Snippet ?? "（讀不到信件內容）";
        }
        else
        {
            content = MailBodyExtractor.Extract(body, contentFields, out var missing);
            // 打錯欄位名稱、或那封信裡根本沒這個欄位時要講出來，否則使用者只會覺得「怎麼少一列」
            if (missing.Count > 0)
                content += $"\n⚠ 這幾個欄位在這封信裡找不到：{string.Join("、", missing)}";
        }

        // 實際推播只處理近 RecentDaysWindow 天內的信，這裡找到的卻可能是更早以前的最新一封符合信件。
        // 不提醒的話，使用者會看到測試成功、卻永遠等不到這封信被自動推播，誤以為規則設錯了。
        if (msg.InternalDate is { } epochMs)
        {
            var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
            if (DateTimeOffset.Now - receivedAt > TimeSpan.FromDays(RecentDaysWindow))
                content += $"\n⚠ 這封信收件於 {receivedAt.ToLocalTime():yyyy/MM/dd}，" +
                           $"超過 {RecentDaysWindow} 天，不會被自動推播（只是拿來驗證擷取結果）。";
        }

        return new GmailRulePreview(Header("From") ?? "(未知寄件者)", Header("Subject") ?? "(無主旨)", content);
    }

    /// <summary>遞迴找出指定 MIME 型別段落的內容（multipart 郵件的正文可能藏在巢狀 part 裡）</summary>
    private static string? FindPart(Google.Apis.Gmail.v1.Data.MessagePart? part, string mimeType)
    {
        if (part is null) return null;

        if (part.MimeType == mimeType && !string.IsNullOrEmpty(part.Body?.Data))
            return MailBodyExtractor.DecodeBase64Url(part.Body.Data);

        if (part.Parts is null) return null;
        foreach (var child in part.Parts)
        {
            var found = FindPart(child, mimeType);
            if (found is not null) return found;
        }
        return null;
    }

    private static void ThrowIfUploadFailed(Google.Apis.Upload.IUploadProgress upload)
    {
        if (upload.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw upload.Exception ?? new InvalidOperationException("上傳未完成，請重試。");
    }
}

/// <summary>
/// 用 Windows DPAPI 加密保存 OAuth token 的 <see cref="IDataStore"/>。
///
/// 加密綁定「本機＋當前 Windows 使用者」，所以這個檔案就算被複製到別台電腦也解不開——
/// 這正是我們不把 token 放進會同步到雲端的 DB 的原因。換電腦時使用者重新授權一次即可。
/// </summary>
internal sealed class DpapiDataStore(string filePath) : IDataStore
{
    public Task StoreAsync<T>(string key, T value)
    {
        var json      = JsonSerializer.Serialize(value);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        return File.WriteAllBytesAsync(filePath, encrypted);
    }

    public Task<T> GetAsync<T>(string key)
    {
        if (!File.Exists(filePath)) return Task.FromResult<T>(default!);
        try
        {
            var decrypted = ProtectedData.Unprotect(
                File.ReadAllBytes(filePath), optionalEntropy: null, DataProtectionScope.CurrentUser);
            var value = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(decrypted));
            return Task.FromResult(value!);
        }
        catch
        {
            // 換過 Windows 使用者／檔案損毀時解不開，視同未授權讓使用者重新連結
            return Task.FromResult<T>(default!);
        }
    }

    public Task DeleteAsync<T>(string key) => ClearAsync();

    public Task ClearAsync()
    {
        if (File.Exists(filePath)) File.Delete(filePath);
        return Task.CompletedTask;
    }
}
