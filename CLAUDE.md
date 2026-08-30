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
- Most tables use plain SQLite `INTEGER PRIMARY KEY` **without** `AUTOINCREMENT`, so IDs are not guaranteed never-reused across the whole DB. Restore therefore lets SQLite assign fresh IDs for every row and rewrites all FK columns *and* embedded JSON id-lists (e.g. `Employee.PreferredShiftIds`, `MonthlySchedule.ExcludeFromAutoAssignIds`) via an old-id→new-id map — never trust the backup JSON's original IDs when reinserting.
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

### Drag-and-drop scheduling is optimistic, not batched

Schedule drag/drop writes to SQLite immediately per-action (optimistic UI + immediate local write), not "edit in memory, save on demand." This is a deliberate choice, not an oversight — batching would require a client-side draft/undo/conflict model for limited benefit since dragging itself never touches the DB mid-gesture.
