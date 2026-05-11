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

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // 確保資料庫已建立，並補齊新增欄位。
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
        var themeService = Services.GetRequiredService<ThemeService>();
        themeService.ApplyCurrent();

        // 套用儲存中的外觀偏好（字體大小 / 字型）。
        var appearanceService = Services.GetRequiredService<AppearanceService>();
        appearanceService.ApplyCurrent();

        // 先關閉自動退出，避免店鋪選擇視窗關閉時整個應用程式提早結束。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var selectionWindow = Services.GetRequiredService<ShopSelectionWindow>();
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
    /// 基準 1366（11吋），上限 1920，夾在 [1.0, 1.4] 之間。
    /// </summary>
    private static void ApplyLayoutScale()
    {
        var screenW = SystemParameters.PrimaryScreenWidth;
        var factor  = Math.Clamp(screenW / 1366.0, 1.0, 1.4);

        var navW    = Math.Round(200 * factor);
        var sideW   = Math.Round(220 * factor);
        var cardW   = Math.Round(240 * factor);
        var hMargin = Math.Round(16  * factor);
        var vMargin = Math.Round(14  * factor);

        Current.Resources["LayoutNavExpandedWidth"]  = navW;
        Current.Resources["LayoutSideListGridWidth"] = new GridLength(sideW);
        Current.Resources["LayoutEmployeeCardWidth"] = cardW;
        Current.Resources["PageMargin"] = new Thickness(hMargin, 6, hMargin, vMargin);
    }

    // 員工識別色色盤（中飽和度、淺色調，確保深色文字可讀）
    internal static readonly string[] EmployeeColorPalette =
    [
        "#A8D8EA", "#A8E6CF", "#FFCCB6", "#C6ADFF", "#FFD3B6",
        "#B5EAD7", "#C7CEEA", "#FFB7C5", "#B5E7B0", "#FAE5A0",
        "#F4C2C2", "#AEE1E1", "#F9D8A0", "#B8D8F8", "#D4EAB0",
        "#F0B8D8", "#B8E8D0", "#E8D0F8", "#F8D0B8", "#C8E8F8",
        "#F8E8B0", "#D0D8F8", "#B8F0D8", "#F8C8C8"
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
            };
            foreach (var (table, col, type) in cols)
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{col}\" {type}";
                    cmd.ExecuteNonQuery();
                }
                catch { /* 欄位已存在時 SQLite 拋出例外，忽略即可 */ }
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
        services.AddTransient<LineService>();
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
