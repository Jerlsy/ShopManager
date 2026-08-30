using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopManager.Data;
using System.Net.Http;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.ViewModels;
using ShopManager.Views;
using ShopManager.Views.EmployeeManagement;
using ShopManager.Views.SalarySettings;
using ShopManager.Views.Schedule;
using ShopManager.Views.ShopSelection;
using ShopManager.Views.ShopSettings;
using ShopManager.Views.ShiftSettings;
using System.Windows;
using System.Windows.Controls;

namespace ShopManager;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 雙擊到看到第一個視窗之間，DI 建置／資料庫遷移／佈景主題套用都是同步工作，
        // 舊電腦上這段空白等待特別明顯。先秒開一個啟動畫面墊著，讓使用者知道程式有在動。
        var splash = new SplashWindow();
        splash.Show();
        splash.SetStatus("正在啟動…");

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // 確保資料庫已建立，並補齊新增欄位。
        splash.SetStatus("正在準備資料庫…");
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            MigrateColumns(db);
            MigrateTables(db);
            MigrateEmployeeColors(db);
        }

        // 依螢幕寬度調整佈局尺寸。
        ApplyLayoutScale();

        // 套用儲存中的主題偏好。
        splash.SetStatus("正在套用佈景主題…");
        var themeService = Services.GetRequiredService<ThemeService>();
        themeService.ApplyCurrent();

        // 套用儲存中的外觀偏好（字體大小 / 字型）。
        var appearanceService = Services.GetRequiredService<AppearanceService>();
        appearanceService.ApplyCurrent();

        // 先關閉自動退出，避免店鋪選擇視窗關閉時整個應用程式提早結束。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 雲端備份是否較新的檢查，改成進到「店鋪與環境設定」頁時再非同步做（見 ShopSettingPage）：
        // 每個店鋪各自綁定 Google 帳號、備份範圍也只有該店鋪自己的資料，啟動當下還沒選店鋪、
        // 根本不知道要查哪一個帳號，硬要在這裡查只能整批撈「有綁過的候選店鋪」逐一嘗試，
        // 不但邏輯繞、之前也踩過同步等待 async 方法造成的死結。

        splash.SetStatus("正在載入店鋪清單…");
        var selectionWindow = Services.GetRequiredService<ShopSelectionWindow>();
        splash.Close();

        var result = selectionWindow.ShowDialog();
        if (result != true)
        {
            Shutdown();
            return;
        }

        var mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    /// <summary>
    /// 依主螢幕邏輯寬度（DIPs，已含 Windows 縮放比例）線性縮放佈局尺寸。
    /// 基準 1366（12吋小型筆電），上限 1920，夾在 [1.0, 1.4] 之間。
    /// 基準尺寸已縮小，讓小螢幕上導航/側欄留白更少、右側主功能區能放大。
    /// </summary>
    private static void ApplyLayoutScale()
    {
        var screenW = SystemParameters.PrimaryScreenWidth;
        var factor  = Math.Clamp(screenW / 1366.0, 1.0, 1.4);

        var navW    = Math.Round(160 * factor);
        var sideW   = Math.Round(220 * 0.75 * factor);
        var cardW   = Math.Round(200 * factor);
        var hMargin = Math.Round(10  * factor);
        var vMargin = Math.Round(8   * factor);

        Current.Resources["LayoutNavExpandedWidth"]  = navW;
        Current.Resources["LayoutSideListGridWidth"] = new GridLength(sideW);
        Current.Resources["LayoutEmployeeCardWidth"] = cardW;
        Current.Resources["PageMargin"] = new Thickness(hMargin, 4, hMargin, vMargin);
    }

    // 員工識別色色盤（深→淺排列，每色相深/中/淺各一，白色文字皆可讀）
    internal static readonly string[] EmployeeColorPalette =
    [
        // 深色（文字用白色清晰可讀）
        "#C62828", "#1565C0", "#2E7D32", "#6A1B9A", "#E65100", "#00695C",
        // 中色
        "#E53935", "#1E88E5", "#43A047", "#8E24AA", "#F57C00", "#00897B",
        // 中淺色
        "#EF5350", "#42A5F5", "#66BB6A", "#AB47BC", "#FFA726", "#26C6DA",
        // 淺色（白色文字仍可讀）
        "#EF9A9A", "#90CAF9", "#A5D6A7", "#CE93D8", "#FFCC80", "#80DEEA"
    ];

    private static void MigrateTables(Data.AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "LineFollowers" (
                    "Id"                INTEGER PRIMARY KEY AUTOINCREMENT,
                    "ShopId"            TEXT    NOT NULL DEFAULT '',
                    "UserId"            TEXT    NOT NULL DEFAULT '',
                    "DisplayName"       TEXT    NOT NULL DEFAULT '',
                    "PictureUrl"        TEXT,
                    "BoundEmployeeId"   INTEGER,
                    "IsBindingDisabled" INTEGER NOT NULL DEFAULT 0,
                    "LastSyncAt"        TEXT    NOT NULL DEFAULT ''
                )
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "ScheduleConflicts" (
                    "Id"           INTEGER PRIMARY KEY AUTOINCREMENT,
                    "ScheduleId"   INTEGER NOT NULL,
                    "EntryId"      INTEGER NOT NULL,
                    "EmployeeId"   INTEGER NOT NULL,
                    "EmployeeName" TEXT    NOT NULL DEFAULT '',
                    "Date"         TEXT    NOT NULL DEFAULT '',
                    "ShiftAlias"   TEXT    NOT NULL DEFAULT '',
                    "Reason"       TEXT    NOT NULL DEFAULT '',
                    FOREIGN KEY ("ScheduleId") REFERENCES "MonthlySchedules"("Id") ON DELETE CASCADE
                )
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "SalaryRecords" (
                    "Id"                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    "ShopId"              TEXT    NOT NULL DEFAULT '',
                    "MonthlyScheduleId"   INTEGER NOT NULL,
                    "Year"                INTEGER NOT NULL,
                    "Month"               INTEGER NOT NULL,
                    "HolidayDates"        TEXT    NOT NULL DEFAULT '[]',
                    "CreatedAt"           TEXT    NOT NULL DEFAULT '',
                    "UpdatedAt"           TEXT    NOT NULL DEFAULT ''
                )
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "SalaryEmployeeRecords" (
                    "Id"              INTEGER PRIMARY KEY AUTOINCREMENT,
                    "SalaryRecordId"  INTEGER NOT NULL,
                    "EmployeeId"      INTEGER NOT NULL,
                    "SalaryType"      INTEGER NOT NULL DEFAULT 0,
                    "HourlyRate"      TEXT    NOT NULL DEFAULT '0',
                    "MonthlyBase"     TEXT    NOT NULL DEFAULT '0',
                    "ContractAmount"  TEXT    NOT NULL DEFAULT '0',
                    "NormalHours"     REAL    NOT NULL DEFAULT 0,
                    "OT1Hours"        REAL    NOT NULL DEFAULT 0,
                    "OT2Hours"        REAL    NOT NULL DEFAULT 0,
                    "RestDayHours"    REAL    NOT NULL DEFAULT 0,
                    "HolidayHours"    REAL    NOT NULL DEFAULT 0,
                    "NormalPay"       TEXT    NOT NULL DEFAULT '0',
                    "OT1Pay"          TEXT    NOT NULL DEFAULT '0',
                    "OT2Pay"          TEXT    NOT NULL DEFAULT '0',
                    "RestDayPay"      TEXT    NOT NULL DEFAULT '0',
                    "HolidayPay"      TEXT    NOT NULL DEFAULT '0',
                    "BaseAmount"      TEXT    NOT NULL DEFAULT '0',
                    FOREIGN KEY ("SalaryRecordId") REFERENCES "SalaryRecords"("Id") ON DELETE CASCADE,
                    FOREIGN KEY ("EmployeeId")     REFERENCES "Employees"("Id")      ON DELETE CASCADE
                )
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "SalaryBonusItems" (
                    "Id"                      INTEGER PRIMARY KEY AUTOINCREMENT,
                    "SalaryEmployeeRecordId"  INTEGER NOT NULL,
                    "Label"                   TEXT    NOT NULL DEFAULT '',
                    "Amount"                  TEXT    NOT NULL DEFAULT '0',
                    "PresetType"              INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY ("SalaryEmployeeRecordId") REFERENCES "SalaryEmployeeRecords"("Id") ON DELETE CASCADE
                )
                """;
            cmd.ExecuteNonQuery();
        }
        finally { conn.Close(); }
    }

    private static void MigrateColumns(Data.AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        conn.Open();
        try
        {
            var cols = new[]
            {
                ("Employees",       "EnglishName",      "TEXT"),
                ("Employees",       "BirthDate",         "TEXT"),
                ("Employees",       "AvatarPhotoData",   "BLOB"),
                ("Employees",       "InterviewDate",     "TEXT"),
                ("Employees",       "ContactInfos",      "TEXT NOT NULL DEFAULT '[]'"),
                ("Employees",       "PreferredShiftIds", "TEXT NOT NULL DEFAULT '[]'"),
                ("Employees",       "ColorHex",          "TEXT NOT NULL DEFAULT ''"),
                ("MonthlySchedules","ShiftDayConfigs",       "TEXT NOT NULL DEFAULT '[]'"),
                ("ShopSettings",    "LogoPhotoData",         "BLOB"),
                ("LaborLawSettings",   "MaxConsecutiveWorkDays", "INTEGER NOT NULL DEFAULT 6"),
                ("LaborLawSettings",   "DailyNormalHours",       "REAL NOT NULL DEFAULT 8.0"),
                ("LaborLawSettings",   "DailyMaxHours",          "REAL NOT NULL DEFAULT 12.0"),
                ("LaborLawSettings",   "WeeklyMaxHours",         "REAL NOT NULL DEFAULT 40.0"),
                ("MonthlySchedules",   "ShiftDateOverrides",     "TEXT NOT NULL DEFAULT '[]'"),
                ("MonthlySchedules",   "StaffingGapDays",        "TEXT NOT NULL DEFAULT '[]'"),
                ("MonthlySchedules",   "EmployeeDayOffs",            "TEXT NOT NULL DEFAULT '[]'"),
                ("MonthlySchedules",   "WorkDayConditionConfigs",    "TEXT NOT NULL DEFAULT '[]'"),
                ("MonthlySchedules",   "EmployeeWorkDays",           "TEXT NOT NULL DEFAULT '[]'"),
                ("MonthlySchedules",   "ExcludeFromAutoAssignIds",   "TEXT NOT NULL DEFAULT '[]'"),
                ("ShopSettings",       "LineChannelAccessToken",      "TEXT"),
                ("ShopSettings",       "LineWorkerUrl",               "TEXT"),
                ("ShopSettings",       "LineWorkerApiKey",            "TEXT"),
                ("ShopSettings",       "LineWelcomeMessage",          "TEXT"),
                ("ShopSettings",       "LineResignMessage",           "TEXT"),
                ("Employees",          "LineUserId",                  "TEXT"),
                ("Employees",          "HolidaySalaryId",             "INTEGER"),
                ("SalaryEmployeeRecords", "HolidayHourlyRate",        "TEXT NOT NULL DEFAULT '0'"),
                ("SalaryEmployeeRecords", "WeekdayHours",             "REAL NOT NULL DEFAULT 0"),
                ("SalaryEmployeeRecords", "WeekdayPay",               "TEXT NOT NULL DEFAULT '0'"),
                ("SalaryEmployeeRecords", "OverridePay",              "TEXT NOT NULL DEFAULT '0'"),
                ("ShopSettings",         "Notes",                    "TEXT"),
                ("ShopSettings",         "OwnerLineBindings",        "TEXT NOT NULL DEFAULT '[]'"),
                ("Employees",            "BankCode",                 "TEXT"),
                ("Employees",            "BankAccount",              "TEXT"),
                ("Employees",            "BankAccountName",          "TEXT"),
                ("SalaryEmployeeRecords", "IsPaid",                  "INTEGER NOT NULL DEFAULT 0"),
                ("SalaryEmployeeRecords", "PaidAt",                  "TEXT"),
                ("ShopSettings",         "GoogleAccountEmail",       "TEXT"),
                ("ShopSettings",         "GoogleDriveLastSyncedRemoteModifiedTime", "TEXT"),
                ("ShopSettings",         "GmailForwardRules",        "TEXT NOT NULL DEFAULT '[]'"),
            };
            // 每個資料表只查一次現有欄位（PRAGMA table_info），只對真正缺少的欄位下 ALTER TABLE。
            // 舊作法是每個候選欄位都直接 ALTER、失敗（欄位已存在）就吃例外——在全新安裝或已升級過的資料庫上，
            // 這代表每次啟動都要拋近 35 次例外，例外的堆疊回溯成本遠高於查一次 PRAGMA。
            var existingByTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in cols.Select(c => c.Item1).Distinct())
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
                    using var reader = cmd.ExecuteReader();
                    int nameOrdinal = reader.GetOrdinal("name");
                    while (reader.Read())
                        set.Add(reader.GetString(nameOrdinal));
                }
                existingByTable[table] = set;
            }

            foreach (var (table, col, type) in cols)
            {
                if (existingByTable.TryGetValue(table, out var existing) && existing.Contains(col))
                    continue;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{col}\" {type}";
                cmd.ExecuteNonQuery();
            }
        }
        finally { conn.Close(); }
    }

    private static void MigrateEmployeeColors(Data.AppDbContext db)
    {
        var employees = db.Employees.Where(e => e.ColorHex == "").ToList();
        for (int i = 0; i < employees.Count; i++)
            employees[i].ColorHex = EmployeeColorPalette[i % EmployeeColorPalette.Length];
        if (employees.Count > 0) db.SaveChanges();
    }

private static void ConfigureServices(ServiceCollection services)
    {
        // 資料庫內容。
        services.AddDbContext<AppDbContext>(ServiceLifetime.Transient);

        // 共用 HttpClient（跨 ViewModel / Service 複用，避免 socket 耗盡）。
        services.AddSingleton<HttpClient>();

        // 應用程式共用服務。
        services.AddSingleton<AppSnackbarService>();
        services.AddSingleton<IAppSnackbarService>(p => p.GetRequiredService<AppSnackbarService>());
        services.AddSingleton<IAppDialogService, AppDialogService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AppearanceService>();
        services.AddSingleton<NavigationService>();

        // 保存目前選取店鋪的共用內容。
        services.AddSingleton<ShopContext>();

        // 商業邏輯服務。
        services.AddTransient<ShopSettingService>();
        services.AddTransient<ShopDataPortabilityService>();
        services.AddTransient<GoogleDriveSyncService>();
        services.AddTransient<LineService>();
        services.AddTransient<IbonPrintService>();
        services.AddTransient<LineFollowerService>();
        services.AddTransient<LineFollowerDialogViewModel>();
        services.AddTransient<Views.Line.LineFollowerWindow>();
        services.AddTransient<ShiftSettingService>();
        services.AddTransient<SalarySettingService>();
        services.AddTransient<EmployeeService>();
        services.AddTransient<MonthlyScheduleService>();
        services.AddTransient<ScheduleService>();
        services.AddTransient<ScheduleConflictService>();
        services.AddTransient<SalaryCalculationService>();
        services.AddTransient<AutoScheduleService>();
        services.AddSingleton<BankCodeService>();

        // ViewModel。
        services.AddTransient<MainViewModel>();
        services.AddSingleton<SystemSettingViewModel>();
        services.AddTransient<ShiftSettingViewModel>();
        services.AddTransient<SalarySettingViewModel>();
        services.AddTransient<EmployeeViewModel>();
        services.AddTransient<ScheduleViewModel>();
        services.AddTransient<ShopSelectionViewModel>();
        services.AddTransient<SalaryViewModel>();

        // 頁面。
        services.AddTransient<ShopSettingPage>();
        services.AddTransient<ShiftSettingPage>();
        services.AddTransient<SalarySettingPage>();
        services.AddTransient<EmployeeListPage>();
        services.AddTransient<SchedulePage>();
        services.AddTransient<Views.Salary.SalaryPage>();

        // 視窗。
        services.AddSingleton<MainWindow>();
        services.AddTransient<ShopSelectionWindow>();
    }
}
