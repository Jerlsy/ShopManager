# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```powershell
dotnet build                          # compile (single-project solution, no separate test project exists)
dotnet run --project ShopManager.csproj   # run the WPF app
dotnet publish ShopManager.csproj -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o <dir>
```

There is no test project and no lint config (no `.editorconfig`, no Cursor/Copilot rules). `publish.ps1` (gitignored — contains a live GitHub token, never commit it) builds, packages with Inno Setup (`ShopManager.iss`), and uploads a GitHub Release; it's invoked from Visual Studio's publish profile, not normally run by hand.

If the app is already running, `dotnet build` fails to copy the exe (file lock) even though compilation succeeded — check for `error CS`/`error MSB3027` specifically rather than treating any build failure as a compile error.

Cloud sessions building here: this is a WPF project (`net10.0-windows`), so `dotnet build` on Linux needs `-p:EnableWindowsTargeting=true` (compiles fine; the app still can't actually run/show UI on Linux — no Windows Desktop runtime).

## Git 分支作業標準（雲端／本機協作、與 main 同步）

先判斷自己在哪一端：有 `CLAUDE_CODE_REMOTE_SESSION_ID` 環境變數 → 雲端；能跑 Windows 上的 VS/dotnet（實際建置+跑）→ 本機。兩端對「跟 main 同步」的職責不同，不要互相代勞。

**雲端這邊（每次作業前）**
1. `git fetch origin main`，比對 main 有沒有更新。
   - 有更新 → 先確認本機（工作目錄）乾淨，`git merge origin/main` 合併回目前的開發分支。**不能用 `rebase` 或 force push 改寫已推送的歷史。**
   - 沒更新 → 直接在開發分支繼續。
   - 本機併回 main 時會 `git rm` 掉 `CloudTest/`，所以合併（常是 fast-forward）後開發分支的 `CloudTest/` 也會消失 → 用 `git checkout <合併前的分支 commit> -- CloudTest/` 還原，並單獨 commit（該 commit 同樣不併回 main）。
2. 雲端**不可推 main**——雲端沒辦法建置測試（WPF 應用無法在 Linux 上實際執行/驗證 UI），併回 main 一律交給本機處理。

**本機這邊（每次作業前）**
1. 確認本機乾淨，`git fetch origin`。
2. 看有沒有還沒併進 main 的開發分支：`git branch -r --no-merged origin/main`。
3. 有的話：切過去、建置、跑測試。
   - 失敗 → 在該開發分支上修，**不能併**。
   - 通過 → 併入 main 前要先取得使用者明確同意，用 `git merge --no-ff --no-commit` 合併，且要 `git rm` 掉 `CloudTest/results/`（那是一次性測試結果，不併進 main）。

**共通規則**
- 大型二進位檔（例如給雲端編譯用的 .NET SDK tar 包）固定放在 `CloudTest/` 內，透過 Git LFS 追蹤（雲端環境需先 `apt-get install -y git-lfs && git lfs install && git lfs pull` 才能取得實際內容，否則只會拉到指標檔）。
- 開發分支上任何動到 `CloudTest/` 的 commit，**一律不併回 main**（合併時用 `--no-ff --no-commit`，再手動 `git rm -r CloudTest/` 或至少 `CloudTest/results/` 後才 commit）。

## Architecture

WPF (.NET 10) MVVM app: `CommunityToolkit.Mvvm` for ViewModels/commands, `Microsoft.Extensions.DependencyInjection` for DI (wired in `App.ConfigureServices`), EF Core + SQLite for storage. `NavigationService` caches pages by `Type`; `Loaded` fires again each time a cached page is reattached to the visual tree — this is deliberate (keeps cross-page edits fresh), not a bug to "fix" by adding a guard.

### Multi-shop, single database

All shops share one SQLite file (`%LOCALAPPDATA%\ShopManager\shopmanager.db`). `Shop` is just a row; every other table carries a `ShopId` (Guid) and is filtered by `ShopContext.ShopId` at the service layer — there is no per-database or per-schema isolation between shops. Keep this in mind before assuming "this shop's data" means "this database": Google backup/restore and the Gmail-forward feature both had to be scoped explicitly to one `ShopId` (see below) because a naive whole-file approach leaks/overwrites other shops' data.

### No EF Migrations — manual incremental schema upgrade

Schema changes are NOT done via `dotnet ef migrations`. `App.xaml.cs` runs `EnsureCreated()` then two hand-written steps on every startup:
- `MigrateTables`: `CREATE TABLE IF NOT EXISTS` for tables added after the initial `EnsureCreated()` schema (verbatim SQL strings).
- `MigrateColumns`: a flat list of `(table, column, sqlType)` tuples; checks `PRAGMA table_info` per table once and only `ALTER TABLE ADD COLUMN`s what's actually missing.

**Any new column or table on an existing entity must be added to one of these two lists**, or upgrades from an older installed version will throw `no such column`/`no such table` at runtime — EF's model alone won't create it on a pre-existing DB file. This has been verified end-to-end against real historical schemas (oldest commit onward) and confirmed to fully reconcile.

### EF tracking staleness across cached pages

Because pages are cached (`NavigationService`) and several services hold a `Transient` `AppDbContext` for the page's lifetime, a *tracked* query can return a stale entity from EF's identity map after another page modified the same row via a different `DbContext`. Read paths that must reflect cross-page edits immediately use `AsNoTracking()` explicitly (`ShopSettingService.GetAsync`, `SalarySettingService.GetLaborLawAsync`, `EmployeeService.GetAllWithDetailsNoTrackingAsync`). When adding a new "read settings/lookup data that another page might have just changed" path, default to `AsNoTracking()` unless you specifically need change-tracking for a save-back flow.

### LINE push: images, not Flex Messages, for anything shareable

LINE's Flex Message type cannot be forwarded by the recipient (platform limitation, not a JSON/content issue) — confirmed by testing. Schedules and salary slips are therefore rendered to PNG (`DrawingVisual`/`FormattedText`) and pushed as `type: "image"`, not Flex. Don't reintroduce Flex for anything the recipient might want to forward. Flex-building helpers remain removed except where explicitly still used.

### LINE Worker relay

`ShopSetting.LineWorkerUrl`/`LineWorkerApiKey` point at a Cloudflare Worker that ShopManager uploads rendered images to (LINE's push API needs a public URL, not inline bytes); the Worker stores them temporarily and they're cleaned up after use.

### Google Drive backup/restore — per-shop scoped, not whole-file

`GoogleDriveSyncService` + `ShopDataPortabilityService` back up/restore **one shop's data only**, not the whole DB file, because each shop can bind a different Google account and a whole-file approach would leak/overwrite other shops' rows. Consequences worth knowing before touching this code:
- Export/import walks the same shop-scoped table graph as `AppDbContext.DeleteShopDataAsync`/`DeleteShopContentAsync` (the latter preserves the `Shop` row itself; restore must never delete it or the shop disappears from the selection list).
- IDs are not guaranteed never-reused across the whole DB (tables created by `EnsureCreated()` on some historical schemas may lack `AUTOINCREMENT`, and IDs are per-machine anyway). Restore therefore lets SQLite assign fresh IDs for every row and rewrites all FK columns *and* embedded JSON id-lists (e.g. `Employee.PreferredShiftIds`, `MonthlySchedule.ExcludeFromAutoAssignIds`) via an old-id→new-id map — never trust the backup JSON's original IDs when reinserting.
- Cloud files live in Drive's `appDataFolder` (`drive.appdata` scope) — a space that is isolated per requesting application/OAuth client, invisible even to the same Google account's other apps. This is why the Gmail-forward feature (below) cannot share this storage.

### Gmail → LINE forwarding — config lives in ShopManager, engine lives in Google Apps Script

Rules are authored in ShopManager (`SystemSettingViewModel`'s rule editor) and uploaded as one JSON file to a **regular** Drive space (`drive.file` scope, not `appDataFolder`) so a separately-deployed Google Apps Script — running under the same Google account but a different execution identity — can read it. The Apps Script source is embedded as a C# string constant in `GmailAppsScriptTemplate.cs` and shown to the user via a "deploy guide" dialog for manual copy-paste; ShopManager cannot deploy or update the script itself.

- **One trigger function** (`forwardMail`, every 5 minutes), not two: an earlier tag→then→push two-function split was replaced because consumer Gmail accounts get only 90 min/day of total trigger runtime, and splitting doubled the trigger count and introduced a label-swap race that could double-push. Don't re-split it without re-deriving that quota math.
- Retry/give-up state lives entirely in Gmail labels (`LinePush/Done|Try{n}|Failed/{ruleId}`), not in the status file — this makes it visible/fixable directly in Gmail (remove the `Failed` label to force a retry) and survives independent of ShopManager being open.
- A heartbeat file (separate from the rules file, also in `drive.file` space) is written by the script after every run and read by ShopManager's "check deployment status" — this is how ShopManager can tell "not deployed" apart from "deployed but broken" without any API into Apps Script itself (which isn't practical from a desktop OAuth client).
- Mail body extraction (`MailBodyExtractor.cs` in C#, mirrored by hand in `GmailAppsScriptTemplate.cs`'s JS) intentionally does **not** try to auto-detect "the important part" of an email — different senders' HTML layouts vary too much for a reliable heuristic. Instead it does generic cleanup (HTML→text, strip visually-hidden elements some senders use to inject noise characters, strip forwarding headers) and then applies a user-authored field/template list (`[literal text]` passes through verbatim as a heading; anything else is a field name whose following value is extracted). Keep both copies in sync when changing extraction logic — there is no shared library between C# and Apps Script's JS.
- Queries always include `newer_than:{RECENT_DAYS}d` (currently 7) — without it, the first run after deploying a new rule would push every historical matching email at once.

### ibon cloud printing — reverse-engineered, single-file-per-pincode

`IbonPrintService` reverse-engineers `print.ibon.com.tw`'s web upload flow (no official API available to individuals). One captured constraint: a single pincode can only carry one uploaded file — a second upload against the same pincode is rejected — so multi-page content (e.g. a month's schedule) must be merged into one PDF client-side before uploading, not uploaded as separate images. Upload calls deliberately don't pass `selectType` (paper/color/duplex) unless there's a specific reason to force one — letting the user choose at the kiosk avoids declaring a color mode that contradicts the document's actual content (schedules render shift blocks in color).

### POS punch-record import in salary calculation (optional per run)

The 計算薪資 panel can import a POS-exported punch xlsx (`AttendanceImportService`, parsed as raw zip+XML — no spreadsheet package). Without an import, calculation is exactly the old schedule-only behavior.
- Punches are grouped **per calendar day: earliest = in, latest = out**, ignoring the POS's own in/out pairing. This deliberately repairs the real-world patterns in the export ("forgot to clock out, re-punched in+out at closing"; "pressed 下班 by mistake then 上班"). Doesn't handle overnight shifts (none exist today).
- Pay basis when both schedule and a complete punch exist is the **schedule's** hours (early arrival / late leaving never add pay). Late/early beyond the per-run grace minutes, scheduled-but-no-punch, punch-but-no-schedule, and half punches become `AttendanceIssue`s that the owner must decide one by one before 接受. Until accepted, scheduled days without a complete punch are **not** paid.
- Pending issues live as a JSON column on `SalaryEmployeeRecords` (with the pre-computed `IfCounted` day pay), so accepting needs no salary-setting lookup and backup/restore needs no ID remapping. Fixed-amount decisions become `BonusPresetType.AttendanceAdjust` bonus lines, which are **dropped on recalculation** (the issues are regenerated) to avoid double counting.
- Name matching uses `Employee.ClockName` (falls back to `Name`); owner-picked mappings are written back from the salary page, so the employee page reads `ClockName` with `AsNoTracking` at edit start to avoid overwriting it with a stale tracked value.

### Resignation vs. deletion

`Employee.IsResigned` compares against **today** and is only for the "已離職" badge. Anything month-scoped (salary eligibility, the schedule page's `ActiveEmployees`) must use `IsEmployedDuring(year, month)` — otherwise someone resigning mid-September vanishes from August payroll computed in late September. `ShiftRuleEngine`'s `ResignedRule` (#0, in both move and copy rule sets) blocks shifts after `ResignDate`, which also surfaces existing post-resignation shifts as conflicts.
- Resign/reinstate side effects live in one place (`EmployeeViewModel.ApplyResignChangeAsync`), shared by the card's 離職/復職 chip, the 到職設定 form, and the delete dialog's "改為設定離職": disable/enable the LINE binding, optionally push `ShopSetting.LineResignMessage`, recheck conflicts. The edit-form save calls `LineFollowerService.BindAsync` (which re-enables the binding), so it re-disables it for resigned employees.
- Deleting an employee DB-cascades their schedule entries **and all historical payroll rows** (past months' totals shrink), so delete first shows `EmployeeService.GetHistoryAsync` and steers to resignation. `EmployeeService.DeleteAsync` also clears the no-FK leftovers (`ScheduleConflicts`, `LineFollowers.BoundEmployeeId`) in the same transaction.

### UI performance / size conventions

- **Shadows use `helpers:ShadowBorder`, not `Border.Effect`.** A `DropShadowEffect` on a container re-blurs the whole subtree on any repaint inside it. `ShadowBorder` draws background/border on an internal plate that carries the shadow and overlays content on top; set the `Shadow` property instead of `Effect`. The shared card styles (`SectionCardStyle`, `PageHeaderStyle`, `EditFormCardStyle`, `CardBorderStyle`) already target it.
- **Schedule page builds heavy UI lazily:** the avatar right-click menu is created on `MouseRightButtonUp` (`EntryAvatar_MouseRightButtonUp`), and the shift-block tooltip body is a `DataTemplate` (`ShiftBlockToolTipTemplate`). Don't put `ContextMenu=`/inline tooltip content on per-cell templates — the calendar rebuilds them all on every month change.
- **`MainWindow` is not `AllowsTransparency`** (it forced software-composited full-screen repaints) and constrains maximize to the monitor work area via a `WM_GETMINMAXINFO` hook, so the custom-chrome window no longer covers the taskbar.
- **Tooltip delay** follows the Windows hover time (`App.OnStartup`); WPF's own default is 1 s.
- **Only the Noto Sans TC weights actually used are embedded** (Regular/Medium/SemiBold/Bold, ~28 MB). Before using `FontWeight` Light/ExtraBold/Black/Thin, add the matching `Fonts/*.ttf` back or WPF will synthesize it.
- `ScheduleConflictService.RecheckAsync` clears the `ChangeTracker` after saving; its `DbContext` lives as long as the cached page and would otherwise accumulate tracked conflict rows.

### Drag-and-drop scheduling is optimistic, not batched

Schedule drag/drop writes to SQLite immediately per-action (optimistic UI + immediate local write), not "edit in memory, save on demand." This is a deliberate choice, not an oversight — batching would require a client-side draft/undo/conflict model for limited benefit since dragging itself never touches the DB mid-gesture.
