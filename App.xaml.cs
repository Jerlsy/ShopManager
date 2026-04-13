using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopManager.Data;
using ShopManager.Models;
using ShopManager.Services;
using ShopManager.ViewModels;
using ShopManager.Views;
using ShopManager.Views.ShopSettings;
using ShopManager.Views.ShiftSettings;
using ShopManager.Views.SalarySettings;
using ShopManager.Views.EmployeeManagement;
using ShopManager.Views.Schedule;
using ShopManager.Views.ShopSelection;
using System.Windows;
using Wpf.Ui;

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

        // 確保資料庫已建立
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        // 先顯示店鋪選擇視窗
        var selectionWindow = Services.GetRequiredService<ShopSelectionWindow>();
        var result = selectionWindow.ShowDialog();
        if (result != true)
        {
            Shutdown();
            return;
        }

        // ShopContext 已設定，開啟主視窗
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        // DbContext
        services.AddDbContext<AppDbContext>(ServiceLifetime.Transient);

        // WPF-UI Services
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // ShopContext — Singleton，保存當前選中的店鋪
        services.AddSingleton<ShopContext>();

        // Services
        services.AddTransient<ShopSettingService>();
        services.AddTransient<ShiftSettingService>();
        services.AddTransient<SalarySettingService>();
        services.AddTransient<EmployeeService>();
        services.AddTransient<MonthlyScheduleService>();
        services.AddTransient<ScheduleService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<SystemSettingViewModel>();
        services.AddTransient<ShiftSettingViewModel>();
        services.AddTransient<SalarySettingViewModel>();
        services.AddTransient<EmployeeViewModel>();
        services.AddTransient<ScheduleViewModel>();
        services.AddTransient<ShopSelectionViewModel>();

        // Pages
        services.AddTransient<ShopSettingPage>();
        services.AddTransient<ShiftSettingPage>();
        services.AddTransient<SalarySettingPage>();
        services.AddTransient<EmployeeListPage>();
        services.AddTransient<SchedulePage>();

        // Windows
        services.AddSingleton<MainWindow>();
        services.AddTransient<ShopSelectionWindow>();
    }
}
