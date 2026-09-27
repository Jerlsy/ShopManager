using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ShopManager.Data;
using ShopManager.Models;
using ShopManager.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ShopManager.ViewModels;

public enum LineTestState { None, Testing, Success, Failed }

public partial class SystemSettingViewModel(
    ShopSettingService service,
    IAppSnackbarService snackbarService,
    IAppDialogService dialogService,
    AppDbContext db,
    ShopContext shopContext,
    ThemeService themeService,
    AppearanceService appearanceService,
    LineService lineService,
    GoogleDriveSyncService googleDriveService) : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private byte[]? _logoPhotoData;
    [ObservableProperty] private List<ContactInfo> _contactInfos = new();

    public static List<string> ContactTypes { get; } = new()
    {
        "Email", "Facebook", "Instagram", "Line", "WhatsApp",
        "Telegram", "WeChat", "YouTube", "Twitter/X", "TikTok",
        "官方網站", "其他"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedWeekStartDay))]
    private int _weekStartDay = 1;

    public WeekStartOption? SelectedWeekStartDay
    {
        get => WeekStartOptions.FirstOrDefault(o => o.Value == WeekStartDay);
        set { if (value is not null) WeekStartDay = value.Value; }
    }
    [ObservableProperty] private bool _nationalHolidaysOff = true;

    // ── LINE 推播設定 ────────────────────────────────────────────────────────
    [ObservableProperty] private string _lineChannelAccessToken = string.Empty;
    [ObservableProperty] private string _lineWorkerUrl = string.Empty;
    [ObservableProperty] private string _lineWorkerApiKey = string.Empty;
    [ObservableProperty] private string _lineWelcomeMessage = string.Empty;
    [ObservableProperty] private string _lineResignMessage = string.Empty;
    [ObservableProperty] private List<OwnerLineBinding> _ownerLineBindings = new();

    /// <summary>業主綁定增減後，清單上的推播對象名稱要重新解析（改名／被移除都會反映出來）</summary>
    partial void OnOwnerLineBindingsChanged(List<OwnerLineBinding> value)
    {
        foreach (var item in GmailForwardRuleItems) item.RefreshOwnerNames(value);
    }

    /// <summary>明確綁定要推播的群組／多人聊天室，形狀沿用 OwnerLineBinding（見 ShopSetting.GroupLineBindings）</summary>
    [ObservableProperty] private List<OwnerLineBinding> _groupLineBindings = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureMailForward))]
    private bool _isLineConfigUnlocked;

    // ── Google Drive 備份設定 ───────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoogleLinked), nameof(NeedsGoogleReauthorize), nameof(CanConfigureMailForward))]
    private string? _googleAccountEmail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GoogleLastSyncedLabel))]
    private DateTimeOffset? _googleDriveLastSyncedRemoteModifiedTime;

    [ObservableProperty] private bool _isGoogleBusy;

    /// <summary>本機是否存有此店鋪的 Google 授權憑證（憑證不進 DB，故與 Email 分開判斷）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoogleLinked), nameof(NeedsGoogleReauthorize), nameof(CanConfigureMailForward))]
    private bool _hasGoogleCredential;

    /// <summary>DB 有帳號記錄但本機沒憑證：多半是換了電腦／Windows 使用者，需重新授權</summary>
    public bool NeedsGoogleReauthorize =>
        !string.IsNullOrWhiteSpace(GoogleAccountEmail) && !HasGoogleCredential;

    /// <summary>可執行備份／還原：已記錄帳號且本機憑證有效</summary>
    public bool IsGoogleLinked =>
        !string.IsNullOrWhiteSpace(GoogleAccountEmail) && HasGoogleCredential;

    public string GoogleLastSyncedLabel => GoogleDriveLastSyncedRemoteModifiedTime is { } t
        ? $"上次同步：{t.ToLocalTime():yyyy/MM/dd HH:mm}"
        : "尚未同步過";

    /// <summary>請 View 執行備份／還原（需要進度視窗與程式重啟，屬於 View 的職責）</summary>
    public event EventHandler? GoogleBackupRequested;
    public event EventHandler? GoogleRestoreRequested;

    /// <summary>剛完成帳號連結，請 View 比對一次雲端備份新舊</summary>
    public event EventHandler? GoogleAccountLinked;

    // ── Gmail轉Line推播規則 ──────────────────────────────────────────────────────
    // 區塊需要 Google 備份綁定＋LINE 推播都設定好才能配置，避免規則存在但沒有可用的
    // 憑證/Token 可用。
    public bool CanConfigureMailForward => IsGoogleLinked && IsLineConfigUnlocked;

    public ObservableCollection<GmailForwardRuleItem> GmailForwardRuleItems { get; } = new();

    /// <summary>
    /// 上傳不再綁「是否測試過」——測試只是輔助確認條件寫得對，不該擋住送出。
    /// 沒有任何規則／沒有任何啟用中的規則時仍可上傳，那代表上傳一份空規則讓 Apps Script 停止推播。
    /// </summary>
    public bool CanUploadGmailForwardRules => !IsGmailForwardBusy;

    [ObservableProperty] private bool _isGmailForwardBusy;

    partial void OnIsGmailForwardBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUploadGmailForwardRules));
        UploadGmailForwardRulesCommand.NotifyCanExecuteChanged();
    }

    // ── 規則編輯區（新增／編輯共用，確定後收合並寫回下方清單）────────────────
    [ObservableProperty] private bool _isRuleEditorOpen;

    /// <summary>正在編輯的規則 Id；null 表示這次是新增</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RuleEditorTitle))]
    private string? _editingRuleId;

    public string RuleEditorTitle => EditingRuleId is null ? "新增規則" : "編輯規則";

    [ObservableProperty] private string? _editorSenderContains;
    [ObservableProperty] private string? _editorSubjectContains;

    /// <summary>內容擷取關鍵字，UI 上以逗號／頓號／換行分隔輸入</summary>
    [ObservableProperty] private string? _editorContentFields;

    [ObservableProperty] private bool _isEditorTesting;
    [ObservableProperty] private string? _editorTestResultMessage;

    /// <summary>編輯區的推播對象複選清單</summary>
    public ObservableCollection<GmailOwnerOption> EditorOwnerOptions { get; } = new();
    [ObservableProperty] private bool _isDeployStatusChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForwardHealthLabel))]
    private GmailServiceHealth _forwardHealth = GmailServiceHealth.NotDeployed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForwardHealthLabel))]
    private DateTimeOffset? _forwardLastRunAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForwardHealthLabel))]
    private string? _forwardError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForwardLastRunSummary))]
    private int _pushedCountLastRun;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForwardLastRunSummary))]
    private int _givenUpCountLastRun;

    public string ForwardHealthLabel => HealthLabel(ForwardHealth, ForwardLastRunAt, ForwardError);

    public string ForwardLastRunSummary => GivenUpCountLastRun > 0
        ? $"上一輪推播 {PushedCountLastRun} 封；{GivenUpCountLastRun} 封重試失敗已放棄（在 Gmail 撕掉 LinePush/Failed 標籤可重推）"
        : $"上一輪推播 {PushedCountLastRun} 封";

    /// <summary>
    /// 狀態文字。必須容忍「health 已更新但 lastRunAt 還沒跟上」的中間狀態——每個屬性各自
    /// 觸發一次重算，賦值途中一定會出現兩者不一致的瞬間，這裡不能假設有值就直接 .Value。
    /// </summary>
    private static string HealthLabel(GmailServiceHealth health, DateTimeOffset? lastRunAt, string? error)
    {
        var runAt = lastRunAt is { } t ? $"（最後執行 {t.ToLocalTime():MM/dd HH:mm}）" : "";
        return health switch
        {
            GmailServiceHealth.Healthy => $"✓ 正常{runAt}",
            GmailServiceHealth.Stale   => $"⚠ 疑似異常，超過 1 天未執行{runAt}",
            GmailServiceHealth.Error   => $"✗ 執行失敗：{error}",
            _                          => "✗ 尚未部署",
        };
    }

    /// <summary>偵測到未部署／異常時觸發，帶上規則 JSON 的 File Id，交由 View 開啟部署指南視窗</summary>
    public event EventHandler<string>? ShowDeployGuideRequested;

    // ── 備註 ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private LineTestState _lineTestState = LineTestState.None;

    // ── 未儲存變更追蹤 ───────────────────────────────────────────────────────
    [ObservableProperty] private bool _hasUnsavedChanges;
    private bool _suppressDirty;
    private bool _closedDayOptionsWired;

    private static readonly HashSet<string?> _volatileProps =
    [
        nameof(HasUnsavedChanges),
        nameof(LineTestState), nameof(LineTestMessage),
        nameof(IsLineTesting), nameof(IsLineTestResultVisible),
        nameof(IsLineTestSuccess), nameof(IsLineTestFailed),
        nameof(CurrentThemeName), nameof(CurrentThemeAccent),
        // Google 設定在連結／備份完成時即時寫入 DB，不該讓畫面顯示「有未儲存變更」
        nameof(GoogleAccountEmail),
        nameof(GoogleDriveLastSyncedRemoteModifiedTime), nameof(GoogleLastSyncedLabel),
        nameof(HasGoogleCredential), nameof(IsGoogleLinked), nameof(NeedsGoogleReauthorize),
        nameof(IsGoogleBusy),
        // Gmail轉Line推播的狀態/衍生屬性也不算「未儲存變更」；規則內容的異動由各 GmailForwardRuleItem
        // 自己的 PropertyChanged 另外接線處理（見 WireRuleItem）
        nameof(CanConfigureMailForward), nameof(CanUploadGmailForwardRules), nameof(IsGmailForwardBusy),
        nameof(IsDeployStatusChecked), nameof(ForwardHealth), nameof(ForwardLastRunAt),
        nameof(ForwardError), nameof(ForwardHealthLabel), nameof(ForwardLastRunSummary),
        nameof(PushedCountLastRun), nameof(GivenUpCountLastRun),
    ];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_suppressDirty && !_volatileProps.Contains(e.PropertyName))
            HasUnsavedChanges = true;
    }
    [ObservableProperty] private string _lineTestMessage = string.Empty;

    /// <summary>測試成功後觸發，View 負責開啟 LineFollowerWindow</summary>
    public event EventHandler<string>? LineTestSucceeded;

    public bool IsLineTesting => LineTestState == LineTestState.Testing;
    public bool IsLineTestResultVisible => LineTestState != LineTestState.None;
    public bool IsLineTestSuccess => LineTestState == LineTestState.Success;
    public bool IsLineTestFailed => LineTestState == LineTestState.Failed;
    [ObservableProperty] private string _customPrimaryHex = "#546E7A";
    [ObservableProperty] private string _customSecondaryHex = "#29B6F6";

    // ── 外觀設定 ────────────────────────────────────────────────────────────
    [ObservableProperty] private double _baseFontSize = 15.0;
    [ObservableProperty] private FontOption? _selectedFontFamily;

    public IReadOnlyList<FontOption> AvailableFontFamilies => AppearanceService.AvailableFonts;

    public ObservableCollection<DayOfWeekOption> ClosedDayOptions { get; } = new()
    {
        new(DayOfWeek.Monday, "週一"),
        new(DayOfWeek.Tuesday, "週二"),
        new(DayOfWeek.Wednesday, "週三"),
        new(DayOfWeek.Thursday, "週四"),
        new(DayOfWeek.Friday, "週五"),
        new(DayOfWeek.Saturday, "週六"),
        new(DayOfWeek.Sunday, "週日"),
    };

    public static List<WeekStartOption> WeekStartOptions { get; } = new()
    {
        new(0, "週日"),
        new(1, "週一"),
    };

    public IReadOnlyList<ThemePreset> ThemePresets => themeService.Presets;
    public string CurrentThemeName => themeService.CurrentThemeName;
    public AppThemeAccent CurrentThemeAccent => themeService.CurrentTheme;

    public async Task LoadAsync()
    {
        _suppressDirty = true;
        try
        {
            var setting = await service.GetAsync();
            if (setting is not null)
            {
                Name = setting.Name;
                Address = setting.Address;
                Phone = setting.Phone;
                LogoPhotoData = setting.LogoPhotoData;
                ContactInfos = new List<ContactInfo>(setting.ContactInfos);
                WeekStartDay = setting.WeekStartDay;
                NationalHolidaysOff = setting.NationalHolidaysOff;

                foreach (var option in ClosedDayOptions)
                    option.IsChecked = setting.ClosedDaysOfWeek.Contains((int)option.Day);

                LineChannelAccessToken = setting.LineChannelAccessToken ?? string.Empty;
                LineWorkerUrl = setting.LineWorkerUrl ?? string.Empty;
                LineWorkerApiKey = setting.LineWorkerApiKey ?? string.Empty;
                LineWelcomeMessage = setting.LineWelcomeMessage ?? string.Empty;
                LineResignMessage = setting.LineResignMessage ?? string.Empty;
                IsLineConfigUnlocked = !string.IsNullOrWhiteSpace(setting.LineChannelAccessToken);
                OwnerLineBindings = new List<OwnerLineBinding>(setting.OwnerLineBindings);
                GroupLineBindings = new List<OwnerLineBinding>(setting.GroupLineBindings);

                GoogleAccountEmail = setting.GoogleAccountEmail;
                GoogleDriveLastSyncedRemoteModifiedTime = setting.GoogleDriveLastSyncedRemoteModifiedTime;
                HasGoogleCredential = googleDriveService.HasLocalCredential();

                GmailForwardRuleItems.Clear();
                foreach (var rule in setting.GmailForwardRules)
                {
                    var item = new GmailForwardRuleItem(rule);
                    item.RefreshOwnerNames(OwnerLineBindings);
                    WireRuleItem(item);
                    GmailForwardRuleItems.Add(item);
                }

                Notes = setting.Notes;
            }

            CustomPrimaryHex = themeService.CustomPrimaryHex;
            CustomSecondaryHex = themeService.CustomSecondaryHex;
            NotifyThemeChanged();

            BaseFontSize = appearanceService.BaseFontSize;
            SelectedFontFamily = AppearanceService.AvailableFonts
                .FirstOrDefault(f => f.Name == appearanceService.FontFamilyName)
                ?? AppearanceService.AvailableFonts[0];

            if (!_closedDayOptionsWired)
            {
                foreach (var opt in ClosedDayOptions)
                    opt.PropertyChanged += (_, _) => { if (!_suppressDirty) HasUnsavedChanges = true; };
                _closedDayOptionsWired = true;
            }
        }
        finally
        {
            _suppressDirty = false;
            HasUnsavedChanges = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var closedDays = ClosedDayOptions
            .Where(o => o.IsChecked)
            .Select(o => (int)o.Day)
            .ToList();

        var setting = new ShopSetting
        {
            Name = Name,
            Address = Address,
            Phone = Phone,
            LogoPhotoData = LogoPhotoData,
            ContactInfos = ContactInfos,
            WeekStartDay = WeekStartDay,
            ClosedDaysOfWeek = closedDays,
            NationalHolidaysOff = NationalHolidaysOff,
            LineChannelAccessToken = string.IsNullOrWhiteSpace(LineChannelAccessToken) ? null : LineChannelAccessToken,
            LineWorkerUrl = string.IsNullOrWhiteSpace(LineWorkerUrl) ? null : LineWorkerUrl,
            LineWorkerApiKey = string.IsNullOrWhiteSpace(LineWorkerApiKey) ? null : LineWorkerApiKey,
            LineWelcomeMessage = string.IsNullOrWhiteSpace(LineWelcomeMessage) ? null : LineWelcomeMessage,
            LineResignMessage = string.IsNullOrWhiteSpace(LineResignMessage) ? null : LineResignMessage,
            OwnerLineBindings = OwnerLineBindings,
            GroupLineBindings = GroupLineBindings,
            GoogleAccountEmail = string.IsNullOrWhiteSpace(GoogleAccountEmail) ? null : GoogleAccountEmail,
            GoogleDriveLastSyncedRemoteModifiedTime = GoogleDriveLastSyncedRemoteModifiedTime,
            GmailForwardRules = GmailForwardRuleItems.Select(r => r.ToModel()).ToList(),
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes,
        };

        await service.SaveAsync(setting);

        // 套用外觀設定
        appearanceService.SetBaseFontSize(BaseFontSize);
        if (SelectedFontFamily is not null)
            appearanceService.SetFontFamily(SelectedFontFamily.Name);

        WeakReferenceMessenger.Default.Send(new SystemConfiguredMessage { ShopName = Name, LogoPhotoData = LogoPhotoData });
        snackbarService.ShowSuccess("店舖設定已儲存");
        HasUnsavedChanges = false;
    }

    public void SetLogoPhoto(byte[] data) => LogoPhotoData = data;

    [RelayCommand]
    public async Task TestLineConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(LineChannelAccessToken))
        {
            LineTestState = LineTestState.Failed;
            LineTestMessage = "請先輸入 Channel Access Token";
            return;
        }
        LineTestState = LineTestState.Testing;
        LineTestMessage = "測試中...";
        OnPropertyChanged(nameof(IsLineTesting));
        OnPropertyChanged(nameof(IsLineTestResultVisible));
        OnPropertyChanged(nameof(IsLineTestSuccess));
        OnPropertyChanged(nameof(IsLineTestFailed));
        var (success, message) = await lineService.TestConnectionAsync(LineChannelAccessToken);
        LineTestState = success ? LineTestState.Success : LineTestState.Failed;
        LineTestMessage = message;
        if (success)
        {
            IsLineConfigUnlocked = true;
            LineTestSucceeded?.Invoke(this, LineChannelAccessToken);
        }
        OnPropertyChanged(nameof(IsLineTesting));
        OnPropertyChanged(nameof(IsLineTestResultVisible));
        OnPropertyChanged(nameof(IsLineTestSuccess));
        OnPropertyChanged(nameof(IsLineTestFailed));
    }

    // ── Google Drive 備份 ───────────────────────────────────────────────────

    /// <summary>
    /// 連結 Google 帳號（開瀏覽器授權）。成功後立即寫入 DB——不等使用者按「儲存所有設定」，
    /// 否則授權完卻忘記存檔時，下次啟動會出現「本機有憑證但 DB 沒帳號」的不一致狀態。
    /// </summary>
    [RelayCommand]
    public async Task LinkGoogleAccountAsync()
    {
        if (IsGoogleBusy) return;
        IsGoogleBusy = true;
        try
        {
            var email = await googleDriveService.LinkAccountAsync();

            GoogleAccountEmail = email;
            HasGoogleCredential = true;

            // 這裡「不」記錄同步時間：本機資料還沒被雲端那份備份覆蓋過，記下去等於謊稱已同步，
            // 之後的新舊比對就不會提示使用者把雲端較新的資料拉下來（換電腦時正是最需要提示的時機）。
            await PersistGoogleSettingsAsync();
            snackbarService.ShowSuccess($"已連結 Google 帳號：{email}");

            // 剛連結完，順手比對一次雲端有沒有更新的資料（設定頁的 Loaded 已經跑過了不會再觸發）
            GoogleAccountLinked?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            snackbarService.ShowWarning("已取消 Google 授權");
        }
        catch (Exception ex)
        {
            snackbarService.ShowError($"連結 Google 帳號失敗：{ex.Message}");
        }
        finally
        {
            IsGoogleBusy = false;
        }
    }

    /// <summary>解除連結：刪除本機憑證並清空 DB 記錄。雲端上的備份檔不會被刪除。</summary>
    [RelayCommand]
    public async Task UnlinkGoogleAccountAsync()
    {
        var confirmed = await dialogService.ShowConfirmAsync(
            "解除 Google 連結",
            "解除後將無法備份或還原資料庫，需要重新授權才能再次使用。\n\n" +
            "雲端上已存在的備份檔不會被刪除。",
            "解除連結", "取消");
        if (!confirmed) return;

        googleDriveService.Unlink();
        HasGoogleCredential = false;
        GoogleAccountEmail = null;
        GoogleDriveLastSyncedRemoteModifiedTime = null;

        await PersistGoogleSettingsAsync();
        snackbarService.ShowSuccess("已解除 Google 連結");
    }

    /// <summary>備份／還原需要進度視窗與程式重啟，交由 View 執行</summary>
    [RelayCommand]
    public void BackupToGoogleDrive() => GoogleBackupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void RestoreFromGoogleDrive() => GoogleRestoreRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>備份完成後由 View 回報雲端檔案時間，更新並保存同步狀態</summary>
    public async Task OnGoogleBackupCompletedAsync(DateTimeOffset modifiedTime)
    {
        GoogleDriveLastSyncedRemoteModifiedTime = modifiedTime;
        await PersistGoogleSettingsAsync();
    }

    /// <summary>
    /// 只保存 Google 相關設定：從 DB 重讀當前設定後改這幾個欄位再存回，
    /// 避免把畫面上其他未儲存的編輯（店名、LINE 設定等）一併寫進 DB。
    /// </summary>
    private async Task PersistGoogleSettingsAsync()
    {
        var setting = await service.GetAsync();
        if (setting is null) return; // 尚未建立店鋪設定，等使用者按「儲存所有設定」時一併寫入

        setting.GoogleAccountEmail = GoogleAccountEmail;
        setting.GoogleDriveLastSyncedRemoteModifiedTime = GoogleDriveLastSyncedRemoteModifiedTime;
        await service.SaveAsync(setting);
    }

    // ── Gmail轉Line推播規則 ──────────────────────────────────────────────────────

    /// <summary>啟用狀態是直接在清單上勾選的，改動要標記未儲存；全部取消啟用時要提醒上傳讓雲端同步停用</summary>
    private void WireRuleItem(GmailForwardRuleItem item)
    {
        // async void 型式的事件處理常式若拋例外會直接讓程式當掉，這裡自行接住
        item.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(GmailForwardRuleItem.IsEnabled)) return;
            if (_suppressDirty) return;

            HasUnsavedChanges = true;
            try { await PromptUploadWhenNoActiveRulesAsync(); }
            catch (Exception ex) { snackbarService.ShowError($"上傳失敗：{ex.Message}"); }
        };
    }

    // ── 規則編輯區 ──────────────────────────────────────────────────────────

    /// <summary>開啟空白編輯區準備新增</summary>
    [RelayCommand]
    public void AddGmailForwardRule()
    {
        EditingRuleId = null;
        EditorSenderContains = null;
        EditorSubjectContains = null;
        EditorContentFields = null;
        EditorTestResultMessage = null;
        BuildEditorOwnerOptions(Array.Empty<string>());
        IsRuleEditorOpen = true;
    }

    /// <summary>把既有規則填回編輯區並展開</summary>
    [RelayCommand]
    public void EditGmailForwardRule(GmailForwardRuleItem item)
    {
        EditingRuleId = item.Id;
        EditorSenderContains = item.SenderContains;
        EditorSubjectContains = item.SubjectContains;
        EditorContentFields = string.Join("\n", item.ContentFields);
        EditorTestResultMessage = null;
        BuildEditorOwnerOptions(item.OwnerLineUserIds);
        IsRuleEditorOpen = true;
    }

    /// <summary>
    /// 把輸入框的內容拆成樣板：一行一項，順序即卡片上的顯示順序。
    /// 不做去重、不排序——這是使用者排好的版面，<c>[文字]</c> 標題行本來就可能重複出現。
    /// </summary>
    private static List<string> ParseFieldNames(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                 .Select(k => k.Trim()).Where(k => k.Length > 0).ToList();

    [RelayCommand]
    public void CancelGmailForwardRuleEdit()
    {
        IsRuleEditorOpen = false;
        EditingRuleId = null;
        EditorTestResultMessage = null;
    }

    /// <summary>確定：把編輯區的內容寫進清單（新增或更新該列），然後收合編輯區。不要求先測試。</summary>
    [RelayCommand]
    public void ConfirmGmailForwardRule()
    {
        if (string.IsNullOrWhiteSpace(EditorSenderContains) && string.IsNullOrWhiteSpace(EditorSubjectContains))
        {
            snackbarService.ShowWarning("請至少輸入寄件者或主旨其中一項條件");
            return;
        }

        var owners = EditorOwnerOptions.Where(o => o.IsSelected).Select(o => o.UserId).ToList();
        if (owners.Count == 0)
        {
            snackbarService.ShowWarning("請至少選擇一位推播對象");
            return;
        }

        var keywords = ParseFieldNames(EditorContentFields);
        var existing = GmailForwardRuleItems.FirstOrDefault(r => r.Id == EditingRuleId);
        if (existing is null)
        {
            var item = new GmailForwardRuleItem(new GmailForwardRule
            {
                SenderContains = EditorSenderContains,
                SubjectContains = EditorSubjectContains,
                OwnerLineUserIds = owners,
                ContentFields = keywords,
            });
            item.RefreshOwnerNames(OwnerLineBindings);
            WireRuleItem(item);
            GmailForwardRuleItems.Add(item);
        }
        else
        {
            existing.SenderContains = EditorSenderContains;
            existing.SubjectContains = EditorSubjectContains;
            existing.OwnerLineUserIds = owners;
            existing.ContentFields = keywords;
            existing.RefreshOwnerNames(OwnerLineBindings);
        }

        HasUnsavedChanges = true;
        IsRuleEditorOpen = false;
        EditingRuleId = null;
    }

    [RelayCommand]
    public async Task RemoveGmailForwardRuleAsync(GmailForwardRuleItem item)
    {
        GmailForwardRuleItems.Remove(item);
        HasUnsavedChanges = true;

        // 正在編輯的就是被刪掉那條 → 收掉編輯區，避免按確定又把它加回來
        if (EditingRuleId == item.Id) CancelGmailForwardRuleEdit();

        await PromptUploadWhenNoActiveRulesAsync();
    }

    /// <summary>
    /// 清單上已經沒有任何啟用中的規則時提醒上傳：雲端規則檔沒跟著更新的話，
    /// Apps Script 會繼續照舊規則推播，畫面上的「停用」等於沒有生效。
    /// </summary>
    private async Task PromptUploadWhenNoActiveRulesAsync()
    {
        if (GmailForwardRuleItems.Any(r => r.IsEnabled)) return;

        var confirmed = await dialogService.ShowConfirmAsync(
            "已沒有啟用中的規則",
            "目前沒有任何啟用中的規則。\n\n" +
            "要立即上傳到雲端，讓 Apps Script 停止推播嗎？\n" +
            "（先不上傳的話，雲端仍是舊的規則，推播會照舊繼續。）",
            "立即上傳", "稍後再說");
        if (confirmed) await UploadGmailForwardRulesAsync();
    }

    /// <summary>依目前的業主綁定建立編輯區的複選清單，並勾選已選中的對象</summary>
    private void BuildEditorOwnerOptions(IEnumerable<string> selectedUserIds)
    {
        var selected = new HashSet<string>(selectedUserIds);
        EditorOwnerOptions.Clear();
        foreach (var binding in OwnerLineBindings)
            EditorOwnerOptions.Add(new GmailOwnerOption(binding.UserId, binding.DisplayName)
            {
                IsSelected = selected.Contains(binding.UserId),
            });
    }

    /// <summary>用編輯區目前的條件直接查 Gmail 撈最新一封符合的信，確認條件寫得對不對（純輔助，不影響能否確定/上傳）</summary>
    [RelayCommand]
    public async Task TestGmailForwardRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(EditorSenderContains) && string.IsNullOrWhiteSpace(EditorSubjectContains))
        {
            EditorTestResultMessage = "請至少輸入寄件者或主旨其中一項條件";
            return;
        }

        IsEditorTesting = true;
        EditorTestResultMessage = null;
        try
        {
            var preview = await googleDriveService.TestGmailRuleAsync(
                EditorSenderContains, EditorSubjectContains, ParseFieldNames(EditorContentFields));
            EditorTestResultMessage = preview is null
                ? "測試成功，但目前查無符合條件的信件"
                : $"最新一封符合的信件：{preview.From}\n主旨：{preview.Subject}\n" +
                  $"── 實際會推播的內容 ──\n{preview.Snippet}";
        }
        catch (Exception ex)
        {
            EditorTestResultMessage = $"測試失敗：{ex.Message}";
        }
        finally
        {
            IsEditorTesting = false;
        }
    }

    /// <summary>把啟用中的規則（含 LINE token）序列化上傳到雲端固定檔名，供 Apps Script 讀取</summary>
    [RelayCommand(CanExecute = nameof(CanUploadGmailForwardRules))]
    public async Task UploadGmailForwardRulesAsync()
    {
        IsGmailForwardBusy = true;
        try
        {
            // 顯示名稱取自綁定清單（規則本身只存 UserId，避免業主改名後兩邊不同步）；
            // 找不到對應綁定的 UserId 直接略過——業主已被移除，推過去也只會失敗。
            var nameByUserId = OwnerLineBindings.ToDictionary(b => b.UserId, b => b.DisplayName);
            var rules = GmailForwardRuleItems
                .Where(r => r.IsEnabled)
                .Select(r => new GmailForwardRuleUpload(
                    r.Id, r.SenderContains, r.SubjectContains,
                    r.OwnerLineUserIds
                        .Where(nameByUserId.ContainsKey)
                        .Select(id => new GmailForwardOwner(id, nameByUserId[id]))
                        .ToList(),
                    r.ContentFields))
                .Where(r => r.Owners.Count > 0)
                .ToList();

            await googleDriveService.UploadMailRulesAsync(rules, LineChannelAccessToken);

            await PersistGmailForwardRulesAsync();
            snackbarService.ShowSuccess(rules.Count == 0
                ? "已上傳空規則，Apps Script 將停止推播"
                : $"已上傳 {rules.Count} 條Gmail轉Line推播規則到雲端");
        }
        catch (Exception ex)
        {
            snackbarService.ShowError($"上傳失敗：{ex.Message}");
        }
        finally
        {
            IsGmailForwardBusy = false;
        }
    }

    /// <summary>只保存Gmail轉Line推播規則（含停用中的），避免蓋掉畫面上其他未儲存的編輯</summary>
    private async Task PersistGmailForwardRulesAsync()
    {
        var setting = await service.GetAsync();
        if (setting is null) return;
        setting.GmailForwardRules = GmailForwardRuleItems.Select(r => r.ToModel()).ToList();
        await service.SaveAsync(setting);
    }

    /// <summary>讀取雲端心跳檔，判斷兩個 Apps Script 服務是否正常；未部署或異常時開啟部署指南</summary>
    [RelayCommand]
    public async Task CheckGmailDeployStatusAsync()
    {
        IsGmailForwardBusy = true;
        try
        {
            var status = await googleDriveService.GetDeployStatusAsync();

            // 時間與錯誤先設，最後才設 Health：每次賦值都會觸發標籤重算，
            // 先把 Health 設好會讓中間那次重算讀到還沒更新的時間。
            ForwardLastRunAt = status.LastRunAt;
            ForwardError = status.LastError;
            ForwardHealth = status.Health;

            PushedCountLastRun = status.PushedCountLastRun;
            GivenUpCountLastRun = status.GivenUpCountLastRun;

            IsDeployStatusChecked = true;

            if (!status.ConfigUploaded)
            {
                snackbarService.ShowWarning("尚未上傳過規則，請先新增並上傳規則");
            }
            else if (status.Health != GmailServiceHealth.Healthy)
            {
                ShowDeployGuideRequested?.Invoke(this, status.ConfigFileId ?? "");
            }
            else
            {
                snackbarService.ShowSuccess("郵件轉推播服務運作正常");
            }
        }
        catch (Exception ex)
        {
            snackbarService.ShowError($"檢查部署狀態失敗：{ex.Message}");
        }
        finally
        {
            IsGmailForwardBusy = false;
        }
    }

    [RelayCommand]
    public void AddContact()
    {
        ContactInfos = new List<ContactInfo>(ContactInfos) { new ContactInfo() };
    }

    [RelayCommand]
    public void RemoveContact(ContactInfo contact)
    {
        var list = new List<ContactInfo>(ContactInfos);
        list.Remove(contact);
        ContactInfos = list;
    }

    /// <summary>新增業主綁定。回傳 false 表示該 UserId 已存在（不重複加入）</summary>
    public bool AddOwnerBinding(OwnerLineBinding item)
    {
        if (OwnerLineBindings.Any(b => b.UserId == item.UserId)) return false;
        OwnerLineBindings = new List<OwnerLineBinding>(OwnerLineBindings) { item };
        return true;
    }

    [RelayCommand]
    public void RemoveOwnerBinding(OwnerLineBinding item)
    {
        var list = new List<OwnerLineBinding>(OwnerLineBindings);
        list.Remove(item);
        OwnerLineBindings = list;
    }

    /// <summary>新增群組/多人聊天室綁定。回傳 false 表示該 Id 已存在（不重複加入）</summary>
    public bool AddGroupBinding(OwnerLineBinding item)
    {
        if (GroupLineBindings.Any(b => b.UserId == item.UserId)) return false;
        GroupLineBindings = new List<OwnerLineBinding>(GroupLineBindings) { item };
        return true;
    }

    [RelayCommand]
    public void RemoveGroupBinding(OwnerLineBinding item)
    {
        var list = new List<OwnerLineBinding>(GroupLineBindings);
        list.Remove(item);
        GroupLineBindings = list;
    }

    [RelayCommand]
    public async Task CloseShopAsync()
    {
        var shopName = shopContext.ShopName;

        var confirmed = await dialogService.ShowConfirmAsync(
            "關閉店鋪",
            $"確定要關閉「{shopName}」嗎？\n\n" +
            "此操作將永久刪除該店鋪的所有資料，包含：\n" +
            "  • 班別設定\n  • 薪資設定\n  • 員工資料\n  • 排班記錄\n\n" +
            "此操作無法復原，請謹慎確認。",
            "確認關閉", "取消");

        if (!confirmed) return;

        await db.DeleteShopDataAsync(shopContext.ShopId);
        WeakReferenceMessenger.Default.Send(new ShopClosedMessage());
    }

    [RelayCommand]
    public void SetSkyBlueAccent() => ApplyTheme(AppThemeAccent.SkyBlue);

    [RelayCommand]
    public void SetMintGreenAccent() => ApplyTheme(AppThemeAccent.MintGreen);

    [RelayCommand]
    public void SetAmberOrangeAccent() => ApplyTheme(AppThemeAccent.AmberOrange);

    [RelayCommand]
    public void SetRoyalPurpleAccent() => ApplyTheme(AppThemeAccent.RoyalPurple);

    [RelayCommand]
    public void SetSoftPinkAccent() => ApplyTheme(AppThemeAccent.SoftPink);

    [RelayCommand]
    public void SetVibrantRedAccent() => ApplyTheme(AppThemeAccent.VibrantRed);

    [RelayCommand]
    public void SetOceanBlueAccent() => ApplyTheme(AppThemeAccent.OceanBlue);

    [RelayCommand]
    public void SetMidnightCyanAccent() => ApplyTheme(AppThemeAccent.MidnightCyan);

    [RelayCommand]
    public void ApplyCustomAccent()
    {
        if (!themeService.TrySetCustomAccent(CustomPrimaryHex, CustomSecondaryHex))
        {
            snackbarService.ShowError("自訂色碼格式錯誤，請輸入像 #1976D2 這樣的 HEX 色碼。");
            return;
        }

        CustomPrimaryHex = themeService.CustomPrimaryHex;
        CustomSecondaryHex = themeService.CustomSecondaryHex;
        NotifyThemeChanged();
        snackbarService.ShowSuccess("已套用新的介面配色。");
    }

    private void ApplyTheme(AppThemeAccent accent)
    {
        themeService.SetAccent(accent);
        NotifyThemeChanged();
        snackbarService.ShowSuccess($"已切換為「{themeService.CurrentThemeName}」。");
    }

    private void NotifyThemeChanged()
    {
        OnPropertyChanged(nameof(CurrentThemeName));
        OnPropertyChanged(nameof(CurrentThemeAccent));
    }
}

public record WeekStartOption(int Value, string Label);

/// <summary>
/// Gmail轉Line推播規則的清單列。內容一律透過上方的編輯區修改（見 ConfirmGmailForwardRule），
/// 這裡只有「啟用」是可以直接在列上勾選的。
/// </summary>
public partial class GmailForwardRuleItem : ObservableObject
{
    public string Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConditionSummary))]
    private string? _senderContains;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConditionSummary))]
    private string? _subjectContains;

    [ObservableProperty] private bool _isEnabled = true;

    /// <summary>推播對象的 LINE UserId；顯示名稱另外從綁定清單解析（見 <see cref="RefreshOwnerNames"/>）</summary>
    [ObservableProperty] private List<string> _ownerLineUserIds = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConditionSummary))]
    private List<string> _contentFields = new();

    [ObservableProperty] private string _ownerSummary = "";

    public string ConditionSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(SenderContains))  parts.Add($"寄件者：{SenderContains}");
            if (!string.IsNullOrWhiteSpace(SubjectContains)) parts.Add($"主旨：{SubjectContains}");
            var summary = parts.Count > 0 ? string.Join("　", parts) : "（無條件）";
            if (ContentFields.Count > 0) summary += $"\n擷取：{string.Join("、", ContentFields)}";
            return summary;
        }
    }

    public GmailForwardRuleItem(GmailForwardRule model)
    {
        Id = model.Id;
        _senderContains = model.SenderContains;
        _subjectContains = model.SubjectContains;
        _isEnabled = model.IsEnabled;
        _ownerLineUserIds = new List<string>(model.OwnerLineUserIds);
        _contentFields = new List<string>(model.ContentFields);
    }

    /// <summary>依目前的業主綁定清單重新解析顯示名稱（業主改名或被移除時要重跑）</summary>
    public void RefreshOwnerNames(IEnumerable<OwnerLineBinding> bindings)
    {
        var nameById = bindings.ToDictionary(b => b.UserId, b => b.DisplayName);
        var names = OwnerLineUserIds.Where(nameById.ContainsKey).Select(id => nameById[id]).ToList();

        // 有 UserId 卻查不到綁定 = 該業主已被移除，明講出來，不要讓使用者以為還會推播給他
        var missing = OwnerLineUserIds.Count - names.Count;
        if (missing > 0) names.Add($"（{missing} 位已移除的業主）");

        OwnerSummary = names.Count > 0 ? string.Join("、", names) : "尚未選擇";
    }

    public GmailForwardRule ToModel() => new()
    {
        Id = Id,
        SenderContains = SenderContains,
        SubjectContains = SubjectContains,
        OwnerLineUserIds = new List<string>(OwnerLineUserIds),
        ContentFields = new List<string>(ContentFields),
        IsEnabled = IsEnabled,
    };
}

/// <summary>規則裡的一個可勾選推播對象</summary>
public partial class GmailOwnerOption(string userId, string displayName) : ObservableObject
{
    public string UserId { get; } = userId;
    public string DisplayName { get; } = displayName;

    [ObservableProperty] private bool _isSelected;
}

public partial class DayOfWeekOption : ObservableObject
{
    public DayOfWeek Day { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isChecked;

    public DayOfWeekOption(DayOfWeek day, string label)
    {
        Day = day;
        Label = label;
    }
}
