using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopManager.Data;
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
            MigrateEmployeeColors(db);
        }

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

    // 員工識別色色盤（中飽和度、淺色調，確保深色文字可讀）
    internal static readonly string[] EmployeeColorPalette =
    [
        "#A8D8EA", "#A8E6CF", "#FFCCB6", "#C6ADFF", "#FFD3B6",
        "#B5EAD7", "#C7CEEA", "#FFB7C5", "#B5E7B0", "#FAE5A0",
        "#F4C2C2", "#AEE1E1", "#F9D8A0", "#B8D8F8", "#D4EAB0",
        "#F0B8D8", "#B8E8D0", "#E8D0F8", "#F8D0B8", "#C8E8F8",
        "#F8E8B0", "#D0D8F8", "#B8F0D8", "#F8C8C8"
    ];

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
                ("MonthlySchedules","ShiftDayConfigs",   "TEXT NOT NULL DEFAULT '[]'"),
                ("ShopSettings",    "LogoPhotoData",     "BLOB"),
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
        services.AddTransient<ShiftSettingService>();
        services.AddTransient<SalarySettingService>();
        services.AddTransient<EmployeeService>();
        services.AddTransient<MonthlyScheduleService>();
        services.AddTransient<ScheduleService>();

        // ViewModel。
        services.AddTransient<MainViewModel>();
        services.AddTransient<SystemSettingViewModel>();
        services.AddTransient<ShiftSettingViewModel>();
        services.AddTransient<SalarySettingViewModel>();
        services.AddTransient<EmployeeViewModel>();
        services.AddTransient<ScheduleViewModel>();
        services.AddTransient<ShopSelectionViewModel>();

        // 頁面。
        services.AddTransient<ShopSettingPage>();
        services.AddTransient<ShiftSettingPage>();
        services.AddTransient<SalarySettingPage>();
        services.AddTransient<EmployeeListPage>();
        services.AddTransient<SchedulePage>();

        // 視窗。
        services.AddSingleton<MainWindow>();
        services.AddTransient<ShopSelectionWindow>();
    }
}
