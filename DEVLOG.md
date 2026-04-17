# ShopManager 開發日誌

> **本文件採用 UTF-8 with BOM 編碼。**
> 在 Windows 環境（VS 2026 / .NET 10）下，為確保中文註解與 UI 文字不發生亂碼，**嚴格禁止** 使用無 BOM 的 UTF-8 或 Big5。

---

## 專案概況

| 項目 | 內容 |
|------|------|
| 專案名稱 | ShopManager (店鋪排班管理系統) |
| 開發語言 | C# (.NET 10) |
| IDE | Visual Studio 2026 Community |
| UI 框架 | WPF + MaterialDesignInXaml (Material Design 3 風格) |
| 架構模式 | MVVM (CommunityToolkit.Mvvm 8.3.2) |
| 資料庫 | SQLite (Entity Framework Core 10) |

---

## 編碼守則 (重要)

為了杜絕中文亂碼問題，開發過程中必須遵守以下規範：
1. **強制編碼**：所有 `.cs`, `.xaml`, `.md`, `.json` 檔案必須儲存為 **UTF-8 with BOM**。
2. **工具設定**：在使用 AI 進行程式碼寫入時，必須確保寫入工具支援並維持 BOM 標頭。
3. **亂碼修復**：若發現 `????` 等亂碼，應立即使用修復腳本或手動轉碼，不可直接在其上開發。

---

## 現狀架構與設計

### 1. 主視窗結構 (MainWindow)
*   **導航固定化**：左側導航欄與頂部標題列固定，不隨頁面捲動。
*   **獨立內容區**：右側內容承載區（ContentControl）內建獨立的 `ScrollViewer`（視頁面需求），確保滑鼠滾輪能精確控制內容區域而不會導致導航欄位移。
*   **視窗自訂化**：隱藏標題列，實作自訂的最小化與關閉按鈕，並支援視窗圓角與陰影。

### 2. 動態主題系統 (ThemeService)
*   **品牌色混合**：背景不再是死板的灰色，而是根據選定的 **主色 (Primary Color)** 進行動態混色（20%~45% 比例）。
*   **多組預設 (Presets)**：
    *   **晴空藍 (Sky Blue)** / **薄荷綠 (Mint Green)** / **暖陽橘 (Amber Orange)** 等 7 組明亮淺色模式。
    *   **深夜模式 (Midnight Dark)** 唯一深色模式。
*   **資源刷新**：透過 `PaletteHelper` 修改 Material Design 核心顏色，同時手動覆寫自訂的 `AppShellBackgroundBrush` 等資源。

### 3. 資料與服務層
*   **多店鋪切換**：支援多店鋪資料隔離，啟動時可選取或建立店鋪。
*   **服務定位 (DI)**：全專案使用 `Microsoft.Extensions.DependencyInjection` 進行解耦。
*   **資料儲存**：SQLite 資料庫透過 EF Core 10 進行管理，支援 JSON 轉換器儲存複雜列表資料。

---

## 開發里程碑 (2026-04-15 現狀)

### Phase 1：基礎建設
- [x] .NET 10 + WPF + Material Design 專案初始化。
- [x] 實作 `IAppSnackbarService` 與 `IAppDialogService`。
- [x] 多店鋪選取視窗與資料隔離機制。

### Phase 2：排班核心
- [x] 員工管理 (包含聯絡資訊、排班規則、到職日)。
- [x] 班別設定 (支援顏色標記、多段工作時間)。
- [x] 薪資設定 (符合勞基法之加班與特休邏輯)。
- [x] 智慧排班 (Outlook 風格視圖、自動計算時數、衝突偵測)。

### Phase 3：介面與體驗優化
- [x] 修正垂直捲動條滑鼠滾輪失效問題 (移除巢狀 ScrollViewer)。
- [x] 重新設計 8 組清亮活潑的主題配色。
- [x] 實作動態背景混色，讓淺色模式更有質感。
- [x] 全面修正中文亂碼問題，統一編碼規範。
- [x] 修正 TextBox / ComboBox 未套用 MD3 樣式（無圓角、無內距）問題。

---

## UI 樣式架構守則 (勿隨意修改)

### 全域隱含樣式機制

`App.xaml` 定義了 TextBox、ComboBox、PasswordBox 的**無 key 隱含樣式**，讓全站所有控制項自動繼承 MD3 的圓角、內距與字體：

```xml
<Style TargetType="{x:Type TextBox}" BasedOn="{StaticResource MaterialDesignOutlinedTextBox}">
    <Setter Property="FontFamily" .../>
    <Setter Property="md:TextFieldAssist.TextFieldCornerRadius" Value="5"/>
    ...
</Style>
```

這些隱含樣式 **BasedOn 套件原生的具名 key**（來自 `MaterialDesign3.Defaults.xaml`）。

### 嚴禁製造循環 BasedOn

**禁止**在 `App.xaml` 同一 ResourceDictionary 內同時定義：
1. 隱含樣式 `BasedOn="{StaticResource MaterialDesignOutlinedTextBox}"` ← 參照本地具名 key
2. 本地具名 key `BasedOn="{StaticResource {x:Type TextBox}}"` ← 參照同一隱含樣式

這會形成循環參照，導致 MD3 樣式完全失效，控制項退回 WPF 預設外觀（無圓角、無邊框、無內距）。

### 頁面內 TextBox / ComboBox 寫法

各頁面的 TextBox / ComboBox **不需要**也**不應該**指定 `Style="{StaticResource MaterialDesignOutlinedTextBox}"`，隱含樣式會自動套用：

```xml
<!-- 正確 -->
<TextBox Text="{Binding EditName}"/>

<!-- 錯誤（冗餘，且若 App.xaml 定義了同名本地 key 會觸發循環） -->
<TextBox Text="{Binding EditName}" Style="{StaticResource MaterialDesignOutlinedTextBox}"/>
```

---

## 檔案結構說明

```
ShopManager/
+-- Data/
│   +-- AppDbContext.cs          # EF Core 上下文
+-- Models/
│   +-- ShopContext.cs           # 目前選定的店鋪環境
│   +-- ShopSetting.cs           # 店鋪規則 (起始日、休假日)
+-- Services/
│   +-- ThemeService.cs          # 核心主題管理與混色邏輯
│   +-- NavigationService.cs     # 頁面切換導航
+-- ViewModels/
│   +-- MainViewModel.cs         # 主視窗控端
│   +-- SystemSettingViewModel.cs # 店鋪與主題設定控端
+-- Views/
    +-- MainWindow.xaml          # 固定導航主視窗
    +-- ShopSettings/            # 店鋪與主題設定頁面
```

---

*最後更新時間：2026-04-17*
*由 Claude Code 管理與維護*
