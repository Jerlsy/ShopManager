# ShopManager 開發進度紀錄

> 此檔案供後續開發的 AI 或開發者快速掌握專案狀態，每次重大進度完成後請更新此檔案。
> 開發時若有使用中文使用繁體中文，並且以UTF-8編碼
> 未經同意不要產生測試script或者單一功能說明、文件
---

## 專案概述

| 項目 | 內容 |
|------|------|
| 專案名稱 | ShopManager（店鋪管理系統） |
| 開發語言 | C# (.NET 10) |
| IDE | **Visual Studio 2026 Community（免費版）** |
| UI 框架 | WPF + lepoco/wpfui 4.2.0 |
| 架構模式 | MVVM（CommunityToolkit.Mvvm 8.3.2） |
| 資料儲存 | SQLite（Entity Framework Core 10） |
| 方案路徑 | `Z:\Developer\ShopManager\ShopManager.sln` |

---

## VS 2026 開啟專案步驟

1. 雙擊 `Z:\Developer\ShopManager\ShopManager.sln`
2. VS 2026 會自動還原 NuGet 套件（需要網路）
3. 按 `Ctrl+Shift+B` 建置，確認無錯誤
4. 按 `F5` 執行

> **安裝 VS 2026 時需要勾選的 Workload：**
> - `.NET desktop development`（WPF 必須）
> - `Data storage and processing`（EF Core 工具支援，選配）

---

## 專案目錄結構

```
ShopManager/                         ← Z:\Developer\ShopManager\
├── ShopManager.sln
├── ShopManager.csproj               ← .NET 10, wpfui 4.2.0, EF Core 10
├── .gitignore
├── DEVLOG.md                        ← 本檔案
├── git-init.bat                     ← 雙擊執行 git 初始化
├── App.xaml / App.xaml.cs           ← DI 容器、全域 Converter 資源
├── Resources/
│   ├── app.ico                      ← 多尺寸應用程式圖示（16/32/48/256px）
│   └── app.png                      ← 原始 PNG 圖示
├── Data/
│   └── AppDbContext.cs              ← EF Core DbContext，JSON 轉換 + 種子資料
├── Models/
│   ├── ShopSetting.cs               ← 含行事曆設定欄位（WeekStartDay, ClosedDaysOfWeek 等）
│   ├── ShiftSetting.cs              ← 含 Color 代表色欄位
│   ├── SalarySetting.cs             ← 含 LaborLawSetting、SalaryType enum
│   ├── Employee.cs                  ← 含 CustomContact、ScheduleRule、DefaultBonus
│   ├── MonthlySchedule.cs           ← 月班表容器（年月、店休日、狀態）
│   └── ScheduleEntry.cs             ← 排班記錄（員工 × 日期 × 班別）
├── Services/
│   ├── ShopSettingService.cs
│   ├── ShiftSettingService.cs
│   ├── SalarySettingService.cs
│   ├── EmployeeService.cs
│   ├── ScheduleService.cs           ← 排班 CRUD + 離職排班檢查
│   └── MonthlyScheduleService.cs    ← 月班表 CRUD + 行事曆產生邏輯
├── ViewModels/
│   ├── MainViewModel.cs             ← 首次啟動流程（IsSystemConfigured）
│   ├── SystemSettingViewModel.cs    ← 原 ShopSettingViewModel，含行事曆設定
│   ├── ShiftSettingViewModel.cs     ← 含時間格式驗證、工時計算、代表色選擇
│   ├── SalarySettingViewModel.cs
│   ├── EmployeeViewModel.cs         ← 含 DayOfWeekItem/ShiftCheckItem/ColleagueCheckItem
│   └── ScheduleViewModel.cs         ← Outlook 風格行事曆 + 員工拖放排班
├── Views/
│   ├── MainWindow.xaml/.cs          ← FluentWindow + NavigationView + SnackbarPresenter + ContentDialogHost
│   ├── ShopSettings/                ← 改名為「系統設定」
│   │   └── ShopSettingPage.xaml/.cs
│   ├── ShiftSettings/
│   │   └── ShiftSettingPage.xaml/.cs
│   ├── SalarySettings/
│   │   └── SalarySettingPage.xaml/.cs
│   ├── EmployeeManagement/
│   │   └── EmployeeListPage.xaml/.cs
│   └── Schedule/                    ← 排班管理
│       └── SchedulePage.xaml/.cs
└── Helpers/
    ├── BoolToVisibilityConverter.cs
    ├── NullToStringConverter.cs
    ├── SalaryTypeDisplayConverter.cs
    ├── SalaryTypeVisibilityConverter.cs
    └── AdditionalConverters.cs      ← HexToBrush、ColorMatch、ViewModeVisibility 等
```

---

## 功能完成狀態

### Phase 1 — 基礎設定（已完成）

| 功能 | 狀態 | 備註 |
|------|------|------|
| 店鋪預設設定 | ✅ | 聯絡方式可動態新增/刪除 |
| 班別設定 | ✅ | 時間下拉快選 + 手動輸入 + 驗證 + 工時顯示 |
| 薪資設定 | ✅ | 主頁 + 子頁（勞基法設定），依類型動態顯示欄位 |
| 員工資料管理 | ✅ | 卡片清單 + 完整編輯側欄 + 排班規則三種 UI |
| .NET 10 + WPF-UI 4.2.0 升級 | ✅ | 編譯通過，0 錯誤 |

### Phase 2 — 系統設定改版 + 排班管理 + UX（已完成）

#### 2-1. 系統設定改版（原「店鋪預設設定」）

| 項目 | 狀態 | 說明 |
|------|------|------|
| 頁面改名「系統設定」 | ✅ | 導覽列文字 + 頁面標題 |
| 店鋪設定區塊 | ✅ | 原有欄位（店名、地址、電話、聯絡方式）包進區塊 |
| 行事曆設定區塊 | ✅ | 一周起始日、每周固定店休日、國定假日休假 |
| - 預設一周起始日 | ✅ | 周日 / 周一（二擇一） |
| - 預設每周固定店休日 | ✅ | 周一～周日多選 |
| - 國定假日是否休假 | ✅ | 是 / 否 |

#### 2-2. 班別設定新增代表色

| 項目 | 狀態 | 說明 |
|------|------|------|
| ShiftSetting.Color 欄位 | ✅ | 儲存 hex 色碼（如 `#4A90D9`） |
| 調色盤 UI | ✅ | 20 色預定義色票讓使用者選擇 |

#### 2-3. 排班管理模組

| 項目 | 狀態 | 說明 |
|------|------|------|
| 新增班表流程 | ✅ | 選擇年月 → 帶出行事曆預設 → 使用者調整 → 產生行事曆 |
| 月班表模型 (MonthlySchedule) | ✅ | 儲存年月、店休日、狀態 |
| 排班記錄模型 (ScheduleEntry) | ✅ | 員工 × 日期 × 班別 |
| 行事曆 UI — 月視圖 | ✅ | 7欄 × N週，每日格子內顯示班別色塊 |
| 行事曆 UI — 周視圖 | ✅ | 7欄 × 時間軸，班別以色塊佔據對應時段 |
| 行事曆 UI — 日視圖 | ✅ | 單日 × 時間軸，班別色塊 + 已排員工名字 |
| 班別色塊上色 | ✅ | 依 ShiftSetting.Color 顯示對應顏色 |
| 店休日背景色 | ✅ | 灰階標示 |
| 員工清單面板 | ✅ | 左側列出在職員工 |
| 員工排班規則自動限制 | ✅ | 點選員工 → 不可排的日期+班別自動 disable |
| 員工拖放排班 | ✅ | 左鍵拖放員工名字到指定日期+班別格子 |
| CheckScheduleAfterResignAsync | ✅ | 離職排班檢查已實作 |

#### 2-4. 首次啟動流程

| 項目 | 狀態 | 說明 |
|------|------|------|
| 僅顯示「系統設定」 | ✅ | 未設定前其他導覽項目隱藏 |
| 儲存後展開選單 | ✅ | 透過 WeakReferenceMessenger 通知 MainViewModel |

#### 2-5. 應用程式圖示

| 項目 | 狀態 | 說明 |
|------|------|------|
| app.ico | ✅ | 多尺寸（16/32/48/256px），csproj ApplicationIcon 已設定 |
| app.png | ✅ | 450×450 原始 PNG |

#### 2-6. 通知系統（Snackbar + ContentDialog）

| 項目 | 狀態 | 說明 |
|------|------|------|
| SnackbarPresenter | ✅ | MainWindow.xaml 內建 SnackbarPresenter |
| ContentDialogHost | ✅ | MainWindow.xaml 內建 ContentDialogHost |
| ISnackbarService DI 註冊 | ✅ | Singleton，所有 ViewModel 共用 |
| IContentDialogService DI 註冊 | ✅ | Singleton，所有 ViewModel 共用 |
| 系統設定 — 儲存成功 Snackbar | ✅ | 綠色 Success 通知，3 秒自動消失 |
| 班別設定 — 儲存成功 Snackbar | ✅ | 同上 |
| 班別設定 — 刪除確認 ContentDialog | ✅ | 彈窗確認後才刪除 |
| 薪資設定 — 儲存成功 Snackbar | ✅ | 薪資設定 + 勞基法設定均有 |
| 薪資設定 — 刪除確認 ContentDialog | ✅ | 彈窗確認後才刪除 |
| 員工資料 — 儲存成功 Snackbar | ✅ | 同上 |
| 員工資料 — 刪除確認 ContentDialog | ✅ | 彈窗確認後才刪除 |
| 排班管理 — 建立班表 Snackbar | ✅ | 新增班表成功通知 |

### Phase 3 — 多店鋪支援（已完成）

| 項目 | 狀態 | 說明 |
|------|------|------|
| Shop Model | ✅ | `Models/Shop.cs`，Guid 為主鍵 |
| ShopContext Singleton | ✅ | `Models/ShopContext.cs`，保存當前選擇的店鋪 |
| 各 Model 加入 ShopId FK | ✅ | ShopSetting、ShiftSetting、SalarySetting、Employee、MonthlySchedule |
| AppDbContext 更新 | ✅ | 加 `DbSet<Shop>`，MonthlySchedule 唯一索引改為 (ShopId, Year, Month) |
| 店鋪選擇視窗 | ✅ | `Views/ShopSelection/ShopSelectionWindow`，下拉選擇 + Expander 新增 |
| 啟動流程改版 | ✅ | App.OnStartup() 先顯示店鋪選擇視窗，關閉則離開程式 |
| 所有 Service 加 ShopId 過濾 | ✅ | 5 個 Service 注入 ShopContext，查詢/寫入皆限定當前店鋪 |
| 導覽文字改名 | ✅ | 「系統設定」→「店舖設定」（MainWindow.xaml + ShopSettingPage.xaml） |

### Phase 4 — 未來規劃

| 項目 | 說明 | 優先度 |
|------|------|--------|
| 薪資計算 | 依出勤 × 薪資設定計算應付薪資 | 🟡 中 |
| 發薪日設定 | 時薪制/月薪制可能有不同規則，待設計 | 🟡 中 |
| 主題切換 | Dark/Light/System | 🟢 選配 |

---

## 排班管理設計說明

### 核心概念

排班管理採用 **Outlook 行事曆風格**，將班別視為「會議方塊」，員工視為「與會者」。

### 操作流程

1. **新增班表**：使用者選擇西元年 + 月份
2. **預填設定**：自動帶出「系統設定 > 行事曆設定」的預設值（店休日、一周起始日等）
3. **調整行事曆**：使用者可手動調整該月的店休日（例如臨時休業或補班）
4. **產生行事曆**：系統將所有啟用中的班別，依時間區間自動填入每個工作日（非店休日），形成班別色塊
5. **排班**：左側面板列出在職員工，使用者用滑鼠左鍵拖放員工名字到指定的日期 × 班別格子內
6. **規則檢查**：點選員工時，系統依該員工的排班規則（固定休假、排除班別、不與共事），自動將不可排的格子設為 disable

### 行事曆三種視圖

| 視圖 | 說明 |
|------|------|
| **月視圖** | 7 欄（依一周起始日排列）× N 週，每日格子顯示班別色塊摘要 |
| **周視圖** | 7 欄 × 垂直時間軸，班別色塊依 StartTime~EndTime 佔據對應時段 |
| **日視圖** | 單日 × 垂直時間軸，班別色塊內顯示已排的員工名字 |

### 班別色塊

- 每個班別在 ShiftSetting 新增 `Color` 欄位（hex 色碼，如 `#4A90D9`）
- 行事曆上的班別色塊使用該顏色作為背景色
- 店休日格子使用灰階背景，不顯示班別

---

## 技術備忘

### WPF-UI 4.2.0 升級注意事項（從 3.0.5）

| 變更 | 說明 |
|------|------|
| `ui:Page` 移除 | 頁面改用 `UserControl` 作為根元素 |
| `ToggleSwitch.Header` → `Content` | 屬性改名 |
| `ui:ComboBox` 移除 | 改用標準 WPF `ComboBox` |
| `InfoBar.Footer` 移除 | 內容直接放 InfoBar 子元素 |
| `IPageService` → `INavigationViewPageProvider` | 命名空間 `Wpf.Ui.Abstractions`，方法 `SetPageProviderService()` |
| code-behind 的 `using Wpf.Ui.Controls` → `using System.Windows.Controls` | 頁面基底類別改為 `UserControl` |
| `DocumentPerson24` 不存在 | 改用 `DocumentPerson20` |
| `ContentPresenter` → `ContentDialogHost` | `SetDialogHost()` 使用新的 `ContentDialogHost` 避免過時警告 |

### WPF-UI 通知系統

| 元件 | 用途 | API |
|------|------|-----|
| **Snackbar** | 底部 toast 通知（儲存成功等） | `ISnackbarService.Show(title, message, appearance, icon, timeout)` |
| **ContentDialog** | 置中模態對話框（刪除確認等） | `IContentDialogService.ShowSimpleDialogAsync(options)` |

設定方式：
1. MainWindow.xaml 放置 `<ui:SnackbarPresenter>` 和 `<ui:ContentDialogHost>`
2. App.xaml.cs 註冊 `ISnackbarService` / `IContentDialogService` 為 Singleton
3. MainWindow.xaml.cs 建構子呼叫 `SetSnackbarPresenter()` / `SetDialogHost()`
4. 各 ViewModel 透過建構子注入使用

### NavigationView 頁面 DI
`MainWindow.xaml.cs` 中的 `PageServiceFactory` 實作 `Wpf.Ui.Abstractions.INavigationViewPageProvider`，讓 `NavigationView` 透過 `App.Services` 建立頁面，讓每個頁面可以透過建構子注入 ViewModel。

### 首次啟動流程
- `MainViewModel.IsSystemConfigured` 控制導覽項目的 Visibility
- 系統設定儲存後透過 `WeakReferenceMessenger` 發送 `SystemConfiguredMessage`
- `MainViewModel` 接收訊息後設定 `IsSystemConfigured = true`，展開其他功能表

### EF Core / SQLite
- 複雜型別（`List<T>`）以 JSON 序列化存入單一欄位
- `TimeOnly` EF Core 原生支援，SQLite 儲存為文字
- 初次啟動執行 `db.Database.EnsureCreated()`

### 時間輸入（ShiftSetting）
- ViewModel 用 string 雙向綁定，`TryParseTime()` 驗證 `HH:mm` 格式
- 驗證失敗時屬性回傳錯誤字串，XAML 透過 `NullToVisibilityConverter` 控制顯示
- `WorkHoursDisplay` 屬性自動計算並支援跨日班別

### 排班規則 UI
| 類別 | 用途 |
|------|------|
| `DayOfWeekItem` | 固定休假週幾勾選 |
| `ShiftCheckItem` | 排除班別勾選（來源：啟用中班別） |
| `ColleagueCheckItem` | 不與共事勾選（來源：在職員工，排除自己） |

---

## Git 歷史

| Commit | 說明 |
|--------|------|
| `init` | 專案初始化：所有模組骨架建立 |
| `feat: timepicker + schedule-rules-ui` | 班別時間輸入、員工排班規則 UI |
| `fix: vs2026-compat` | 升級 .NET 9、EF Core 9、DI 套件；修正 MainWindow DI 頁面工廠 |

---

## 下一步建議（給下一位 AI）

1. 接續 Phase 3 開發（薪資計算、發薪日設定）
2. 每完成一個子功能後執行 `dotnet build` 確認編譯通過
3. 排班行事曆已完成基本拖放，可考慮加入右鍵移除排班功能
4. 通知系統已就位，新增功能時可直接注入 `ISnackbarService` / `IContentDialogService` 使用

---

*最後更新：2026-04-14*
*由 Claude AI 自動生成*
