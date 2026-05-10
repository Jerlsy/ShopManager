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

## 開發里程碑

### Phase 1：基礎建設
- [x] .NET 10 + WPF + Material Design 專案初始化。
- [x] 實作 `IAppSnackbarService` 與 `IAppDialogService`。
- [x] 多店鋪選取視窗與資料隔離機制。

### Phase 2：排班核心
- [x] 員工管理（聯絡資訊、排班規則、到職日、大頭貼裁切上傳）。
- [x] 班別設定（顏色標記、工時自動計算）。
- [x] 薪資設定（時薪 / 月薪 / 合約，勞基法加班費率設定）。
- [x] 月曆排班（月 / 週 / 日三種視圖，拖拉移動 / 複製 / 刪除）。

### Phase 3：介面與體驗優化
- [x] 修正垂直捲動條滑鼠滾輪失效問題（移除巢狀 ScrollViewer）。
- [x] 重新設計 8 組清亮活潑的主題配色，實作動態背景混色。
- [x] 全面修正中文亂碼問題，統一 UTF-8 with BOM 編碼規範。
- [x] 修正 TextBox / ComboBox 未套用 MD3 樣式問題。

### Phase 4：排班規則引擎（2026-04-22）
- [x] 新增 `ShiftRuleEngine`，介面化 `IShiftRule`，規則可獨立擴充。
- [x] 規則 #1 `FixedOffRule`：固定休假日封鎖。
- [x] 規則 #2 `ExcludeShiftRule`：排除指定班別。
- [x] 規則 #3 `NotWithRule`：同班次不與特定同事共班（雙向保護）。
- [x] 規則 #4 `ColleagueNotWithRule`：同事設定反向覆蓋。
- [x] 規則 #5 `TimeOverlapRule`：同日時間重疊檢查。
- [x] 規則 #6 `DailyMaxHoursRule`：每日工時上限（依薪資別 / 個人設定）。
- [x] 員工大頭貼點擊 → 居中資訊卡（含移除按鈕）。
- [x] 班表間拖拉移動（點擊 vs 拖曳閾值區分）。
- [x] 店鋪 Logo 裁切上傳（`LogoCropWindow`）。
- [x] 各對話框模組化：`SalarySettingDialog`、`ScheduleRulesDialog`、`ShiftPreferenceDialog`、`EmploymentDialog`。

### Phase 5：衝突檢測、自動排班、規則擴充（2026-05-01）
- [x] **班表衝突檢測**：`ScheduleConflict` 模型 + `ScheduleConflictService`，SchedulePage 標頭顯示衝突計數徽章。
- [x] **排班還原（Undo）**：`UndoCommand`，支援拖移、刪除等操作的還原。
- [x] 規則 #7 `NotWithDayRule`：不與同事同天（跨班別）。
- [x] 規則 #8 `ColleagueNotWithDayRule`：同事設定反向覆蓋（同天）。
- [x] 規則 #9 `ConsecutiveDaysRule`：勞基法第 36 條，最長連續上班日數上限。
- [x] 規則 #10 `WeeklyMaxHoursRule`：每週工時上限。
- [x] 複製模式 `EvaluateForCopy()`：僅跑時間重疊 + 工時上限，不限共事人。
- [x] **自動排班**：多階段演算法（Phase 1 班次填滿 + Phase 2 補排），優先班別公平配額機制。
- [x] 自動排班設定持久化：`WorkDayConditionConfig`、休假日、強制上班日、排除員工、優先班別一次性寫入。
- [x] `LaborLawSetting` 重構：統一工時欄位（`DailyNormalHours / DailyMaxHours / WeeklyMaxHours / MaxConsecutiveWorkDays`）。
- [x] `MonthlySchedule` 擴充：`ShiftDateOverrides`（班別覆蓋）、`StaffingGapDays`（人力不足日）、`EmployeeDayOffs`、`WorkDayConditionConfigs`、`EmployeeWorkDays`、`ExcludeFromAutoAssignIds`。
- [x] `ScheduleViewModels.cs`：抽出 `CalendarDay`、`ShiftBlock`、`EntryItem`、`EmployeeWorkloadItem`、`EmployeeConstraintItem` 等 UI 輔助類別。

### Phase 6：排班 UI 細節修正（2026-05-03）
- [x] **員工詳情同天同仁**：詳情對話框的 Colleagues 從「同班別」改為「同一天任何班別」，加 `DistinctBy`。
- [x] **詳情版面重排**：時間欄固定 130px，同仁頭像欄固定 110px，頭像移至時間右側（不與班別混淆）。
- [x] **頭像 Tooltip**：改用 `SpeechBubbleToolTip`（黃底圓角氣泡），顯示頭像 + 姓名。
- [x] **Tooltip 定位修正（週 / 日視圖）**：週 / 日視圖 shift block 因 `ContentPresenter` + `BlockMargin` 絕對定位導致 `Placement="Top"` 座標偏移，改用 `Placement="Custom"` + `CustomPopupPlacementCallback`，tooltip 固定在 block 正上方水平置中。
- [x] **自動排班鎖定過去日期**：`ClearFutureEntriesAsync` 只清除明天以後的 Entry；`assignedCount` / `assignedPerShift` 從過去鎖定 Entry 起算；`workDays` 跳過今天及以前。
- [x] **週視圖頭像排列**：`UniformGrid Rows="1"` 改為 `StackPanel Orientation="Vertical"`，垂直堆疊顯示。

### Phase 7：薪資計算（規劃中）
- [ ] `SalaryRecord` + `SalaryEmployeeRecord` + `SalaryBonusItem` 資料模型與 Migration。
- [ ] `SalaryCalculationService`：依時薪 / 月薪 / 合約三種類型計算，含勞基法 OT1/OT2/假日費率。
- [ ] 時薪制：店休日 = 假日（HolidayRate）；月薪制：國定假日可逐一勾選是否計加班費。
- [ ] 員工固定休假日有排班 → 視為一般工作日（不加成）。
- [ ] 薪資計算頁面：月份選擇 → 員工卡片（工時明細 + 薪資明細 + 額外項目）→ 儲存。
- [ ] 最低薪資警示。

### Phase 8：LINE 推播強化 — 店主帳號綁定（規劃中）

> 依賴：店鋪設定已完成 LINE Channel Token 設定。

**資料層**
- [ ] 新增 `Models/OwnerLineBinding.cs`（record：`UserId`、`DisplayName`、`PictureUrl`）。
- [ ] `Models/ShopSetting.cs` 新增 `List<OwnerLineBinding> OwnerLineBindings`。
- [ ] `Data/AppDbContext.cs` 對 `OwnerLineBindings` 加 JSON `HasConversion`（同 ContactInfos 模式）。

**ViewModel**
- [ ] `ViewModels/SystemSettingViewModel.cs` 新增 `ObservableCollection<OwnerLineBindingViewModel> OwnerLineBindings`。
- [ ] 新增 `AddOwnerLineBindingCommand`：開啟好友清單（reuse `LineFollowerWindow` 或共用選擇邏輯），選中後呼叫 `ApplyOwnerBinding(userId, displayName, pictureUrl)`。
- [ ] 新增 `RemoveOwnerLineBindingCommand(item)`：從清單移除指定項目。
- [ ] `SaveAsync` / `LoadAsync` 同步讀寫 OwnerLineBindings。

**View**
- [ ] `Views/ShopSettings/ShopSettingPage.xaml`：LINE 設定區塊下方新增「業主 LINE 帳號」子區塊。
  - 清單顯示：頭像縮圖 + DisplayName + UserId（灰字）。
  - 每列右側「移除」按鈕。
  - 左下「＋ 新增業主帳號」按鈕（開啟好友清單）。
- [ ] `Views/ShopSettings/ShopSettingPage.xaml.cs` 新增 binding handler（選取後回呼 ViewModel）。

---

### Phase 9：轉存班表（規劃中）

> 依賴：本月排班資料存在；選用 LINE 推播時需完成 Phase 8（店主帳號綁定）。

**ScheduleViewModel 修改**
- [ ] `ViewModels/Schedule/ScheduleViewModel.cs` 新增 `ShowExportScheduleButton`（bool：本月 `MonthlySchedule` 存在且 Entries 非空）。
- [ ] 新增 `OpenExportScheduleCommand`：開啟 `ExportScheduleWindow`，傳入當月 `MonthlySchedule`。

**SchedulePage.xaml**
- [ ] 自動排班按鈕旁加「轉存班表」按鈕，`Visibility` 綁定 `ShowExportScheduleButton`。

**ExportScheduleViewModel**
- [ ] 新增 `ViewModels/ExportScheduleViewModel.cs`。
  - `SelectedFileFormat`（enum `ExportFormat`：`Txt` / `Excel`）。
  - `ExportRecipients`（`ObservableCollection<ExportRecipientItem>`）：
    - 員工：`LineUserId` 非空 且 本月有 `ScheduleEntry`。
    - 店主：`ShopSetting.OwnerLineBindings` 全部列入（標記來源為「業主」）。
  - `PushType`（enum：`PersonalOnly` 僅個人班表 / `FullSchedule` 本月完整班表）。
  - `ExportToFileCommand`：`SaveFileDialog` → 輸出 `.txt`（格式：表頭日期橫排、員工縱排，每格顯示班別 Alias）。
  - `PushLineCommand`：逐筆呼叫 `LineService.PushMessageAsync`，訊息格式待後續規劃。
  - `ToggleAllCommand`：全選 / 取消全選 ExportRecipients。

**ExportScheduleWindow**
- [ ] 新增 `Views/Schedule/ExportScheduleWindow.xaml(.cs)`，分兩個 section card：
  - **匯出班表**：檔案格式下拉（txt / Excel）+ 「轉存為檔案」按鈕（先實作 txt）。
  - **LINE 推播班表**：可勾選收件人清單（頭像 + 姓名 + 來源標籤）+ 推播類別下拉 + 「確定推播」按鈕。

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

*最後更新時間：2026-05-09*
*由 Claude Code 管理與維護*
