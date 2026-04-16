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

        // 確保資料庫已建立。
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
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
