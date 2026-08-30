using Microsoft.EntityFrameworkCore;
using ShopManager.Data;
using ShopManager.Models;
using System.Text.Json;

namespace ShopManager.Services;

/// <summary>單一店鋪所有資料的可攜表示，供備份／還原使用。所有清單只含這個店鋪自己的資料。</summary>
public class ShopDataBundle
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>店鋪名稱（Shop 本體的 Guid Id 不進來——還原時是匯入到本機既有的那個店鋪，Id 沿用本機的）</summary>
    public string? ShopName { get; set; }

    public ShopSetting? ShopSetting { get; set; }
    public List<ShiftSetting> ShiftSettings { get; set; } = new();
    public List<SalarySetting> SalarySettings { get; set; } = new();
    public List<Employee> Employees { get; set; } = new();
    public List<CustomContact> CustomContacts { get; set; } = new();
    public List<ScheduleRule> ScheduleRules { get; set; } = new();
    public List<DefaultBonus> DefaultBonuses { get; set; } = new();
    public List<MonthlySchedule> MonthlySchedules { get; set; } = new();
    public List<ScheduleEntry> ScheduleEntries { get; set; } = new();
    public List<ScheduleConflict> ScheduleConflicts { get; set; } = new();
    public List<SalaryRecord> SalaryRecords { get; set; } = new();
    public List<SalaryEmployeeRecord> SalaryEmployeeRecords { get; set; } = new();
    public List<SalaryBonusItem> SalaryBonusItems { get; set; } = new();
}

/// <summary>
/// 匯出／匯入單一店鋪的完整資料（用於 Google Drive 備份／還原，取代整檔案 VACUUM INTO 的做法）。
///
/// 為什麼要這樣做：這個 App 所有店鋪共用同一個 SQLite 檔案，ShopId 只是資料表裡的過濾欄位。
/// 如果備份/還原整個檔案，一個店鋪的備份會連帶含有其他店鋪的資料，還原時也會覆蓋掉其他店鋪——
/// 但每個店鋪各自綁定不同的 Google 帳號，這樣「各自獨立」其實不成立。改成只匯出/匯入
/// 該店鋪自己的資料列，才符合「每店鋪一個 Google 帳號、互不影響」的預期。
///
/// 匯入時的關鍵：這些資料表大多是 EF 預設建立的 <c>INTEGER PRIMARY KEY</c>（沒有明確標
/// <c>AUTOINCREMENT</c>），SQLite 配新 Id 的規則是「目前資料表最大值＋1」而非永久遞增的計數器。
/// 如果直接拿備份裡的舊 Id 插回去，極端情況下可能撞到其他店鋪目前正在用的 Id（例如資料表曾經
/// 整個淨空過一次），造成兩筆不相干資料的關聯被靜靜地搞混。因此匯入一律讓資料庫重新配發 Id，
/// 並用「舊 Id → 新 Id」對照表回頭改寫所有外鍵欄位與內嵌的 Id 清單（例如
/// <see cref="Employee.PreferredShiftIds"/>、<see cref="MonthlySchedule.ExcludeFromAutoAssignIds"/>）。
/// </summary>
public class ShopDataPortabilityService(AppDbContext db)
{
    public async Task<string> ExportAsync(Guid shopId, CancellationToken ct = default)
    {
        var bundle = new ShopDataBundle
        {
            ShopName = await db.Shops.AsNoTracking()
                .Where(s => s.Id == shopId).Select(s => s.Name).FirstOrDefaultAsync(ct),
            ShopSetting = await db.ShopSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ShopId == shopId, ct),
            ShiftSettings = await db.ShiftSettings.AsNoTracking()
                .Where(s => s.ShopId == shopId).ToListAsync(ct),
            SalarySettings = await db.SalarySettings.AsNoTracking()
                .Where(s => s.ShopId == shopId).ToListAsync(ct),
        };

        bundle.Employees = await db.Employees.AsNoTracking()
            .Where(e => e.ShopId == shopId).ToListAsync(ct);
        var employeeIds = bundle.Employees.Select(e => e.Id).ToList();

        bundle.CustomContacts = await db.Set<CustomContact>().AsNoTracking()
            .Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync(ct);
        bundle.ScheduleRules = await db.Set<ScheduleRule>().AsNoTracking()
            .Where(r => employeeIds.Contains(r.EmployeeId)).ToListAsync(ct);
        bundle.DefaultBonuses = await db.Set<DefaultBonus>().AsNoTracking()
            .Where(b => employeeIds.Contains(b.EmployeeId)).ToListAsync(ct);

        bundle.MonthlySchedules = await db.MonthlySchedules.AsNoTracking()
            .Where(m => m.ShopId == shopId).ToListAsync(ct);
        var monthlyIds = bundle.MonthlySchedules.Select(m => m.Id).ToList();

        bundle.ScheduleEntries = await db.ScheduleEntries.AsNoTracking()
            .Where(e => monthlyIds.Contains(e.MonthlyScheduleId)).ToListAsync(ct);
        bundle.ScheduleConflicts = await db.ScheduleConflicts.AsNoTracking()
            .Where(c => monthlyIds.Contains(c.ScheduleId)).ToListAsync(ct);

        bundle.SalaryRecords = await db.SalaryRecords.AsNoTracking()
            .Where(r => monthlyIds.Contains(r.MonthlyScheduleId)).ToListAsync(ct);
        var salaryRecordIds = bundle.SalaryRecords.Select(r => r.Id).ToList();

        bundle.SalaryEmployeeRecords = await db.SalaryEmployeeRecords.AsNoTracking()
            .Where(r => salaryRecordIds.Contains(r.SalaryRecordId)).ToListAsync(ct);
        var salaryEmployeeRecordIds = bundle.SalaryEmployeeRecords.Select(r => r.Id).ToList();

        bundle.SalaryBonusItems = await db.SalaryBonusItems.AsNoTracking()
            .Where(b => salaryEmployeeRecordIds.Contains(b.SalaryEmployeeRecordId)).ToListAsync(ct);

        return JsonSerializer.Serialize(bundle);
    }

    /// <summary>
    /// 清空該店鋪現有資料後，依相依順序插入 JSON 內的資料（重新配發 Id，改寫所有關聯）。
    ///
    /// <paramref name="syncedRemoteModifiedTime"/> 是這份備份在雲端的 modifiedTime：匯入完成後會蓋到
    /// 還原出來的 ShopSetting 上。這一步不能省——備份 JSON 是在「上傳之前」匯出的，裡面記的
    /// 同步時間必然比它自己在雲端的 modifiedTime 舊，直接沿用會讓設定頁每次都判定「雲端比較新」
    /// 而重複跳出還原提示。
    /// </summary>
    public async Task ImportAsync(Guid shopId, string json,
        DateTimeOffset? syncedRemoteModifiedTime = null, CancellationToken ct = default)
    {
        var bundle = JsonSerializer.Deserialize<ShopDataBundle>(json)
            ?? throw new InvalidOperationException("備份檔案格式錯誤，已中止還原（本機資料未變動）。");

        // 大量插入時關掉自動變更偵測：EF 每次 Add 都掃一遍已追蹤實體，資料量上萬筆後
        // 這個 O(n²) 行為會變成主要瓶頸（十萬筆等級差距是分鐘 vs 秒）。
        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            await ImportCoreAsync(shopId, bundle, syncedRemoteModifiedTime, ct);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    private async Task ImportCoreAsync(Guid shopId, ShopDataBundle bundle,
        DateTimeOffset? syncedRemoteModifiedTime, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 只清內容、保留 Shop 本體那一列——連 Shop 一起刪掉的話，還原完店鋪會整個從清單上消失
        await db.DeleteShopContentAsync(shopId);

        // 店鋪本體若因故不存在（例如從舊版本的錯誤還原留下的殘局），補一列回去，
        // 避免資料都在、卻沒有店鋪可以選的狀況
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, ct);
        if (shop is null)
            db.Shops.Add(new Shop { Id = shopId, Name = bundle.ShopName ?? "（未命名店鋪）" });
        else if (!string.IsNullOrWhiteSpace(bundle.ShopName))
            shop.Name = bundle.ShopName;
        await db.SaveChangesAsync(ct);

        var shiftMap = await InsertWithNewIdsAsync(bundle.ShiftSettings, s => s.ShopId = shopId, ct);
        var salaryMap = await InsertWithNewIdsAsync(bundle.SalarySettings, s => s.ShopId = shopId, ct);

        var employeeMap = await InsertWithNewIdsAsync(bundle.Employees, e =>
        {
            e.ShopId = shopId;
            e.DefaultShiftId = RemapNullable(shiftMap, e.DefaultShiftId);
            e.DefaultSalaryId = RemapNullable(salaryMap, e.DefaultSalaryId);
            e.HolidaySalaryId = RemapNullable(salaryMap, e.HolidaySalaryId);
            e.PreferredShiftIds = RemapList(shiftMap, e.PreferredShiftIds);
        }, ct);

        await InsertWithNewIdsAsync(bundle.ScheduleRules, r =>
        {
            r.EmployeeId = employeeMap[r.EmployeeId];
            r.ExcludedShiftIds = RemapList(shiftMap, r.ExcludedShiftIds);
            r.ExcludedColleagueIds = RemapList(employeeMap, r.ExcludedColleagueIds);
        }, ct);
        await InsertWithNewIdsAsync(bundle.CustomContacts, c => c.EmployeeId = employeeMap[c.EmployeeId], ct);
        await InsertWithNewIdsAsync(bundle.DefaultBonuses, b => b.EmployeeId = employeeMap[b.EmployeeId], ct);

        var monthlyMap = await InsertWithNewIdsAsync(bundle.MonthlySchedules, m =>
        {
            m.ShopId = shopId;
            foreach (var c in m.ShiftDayConfigs) c.ShiftId = shiftMap.GetValueOrDefault(c.ShiftId, c.ShiftId);
            foreach (var o in m.ShiftDateOverrides) o.ShiftIds = RemapList(shiftMap, o.ShiftIds);
            foreach (var d in m.EmployeeDayOffs) d.EmployeeId = employeeMap.GetValueOrDefault(d.EmployeeId, d.EmployeeId);
            foreach (var w in m.EmployeeWorkDays) w.EmployeeId = employeeMap.GetValueOrDefault(w.EmployeeId, w.EmployeeId);
            m.ExcludeFromAutoAssignIds = RemapList(employeeMap, m.ExcludeFromAutoAssignIds);
        }, ct);

        var entryMap = await InsertWithNewIdsAsync(bundle.ScheduleEntries, e =>
        {
            e.MonthlyScheduleId = monthlyMap[e.MonthlyScheduleId];
            e.EmployeeId = employeeMap[e.EmployeeId];
            e.ShiftSettingId = shiftMap[e.ShiftSettingId];
        }, ct);

        await InsertWithNewIdsAsync(bundle.ScheduleConflicts, c =>
        {
            c.ScheduleId = monthlyMap.GetValueOrDefault(c.ScheduleId, c.ScheduleId);
            c.EntryId = entryMap.GetValueOrDefault(c.EntryId, c.EntryId);
            c.EmployeeId = employeeMap.GetValueOrDefault(c.EmployeeId, c.EmployeeId);
        }, ct);

        var salaryRecordMap = await InsertWithNewIdsAsync(bundle.SalaryRecords, r =>
        {
            r.ShopId = shopId;
            r.MonthlyScheduleId = monthlyMap[r.MonthlyScheduleId];
        }, ct);

        var salaryEmployeeRecordMap = await InsertWithNewIdsAsync(bundle.SalaryEmployeeRecords, r =>
        {
            r.SalaryRecordId = salaryRecordMap[r.SalaryRecordId];
            r.EmployeeId = employeeMap[r.EmployeeId];
        }, ct);

        await InsertWithNewIdsAsync(bundle.SalaryBonusItems,
            b => b.SalaryEmployeeRecordId = salaryEmployeeRecordMap[b.SalaryEmployeeRecordId], ct);

        // DeleteShopContentAsync 已經把這個店鋪原本的 ShopSetting 列刪掉了，這裡一定是新插入，
        // Id 讓資料庫重新配發即可——ShopSetting.Id 本身沒有被其他表當外鍵引用，換掉沒有影響。
        // 備份裡沒有 ShopSetting 時也要補一列，否則同步時間無處可記，設定頁會一直重複問要不要還原。
        var setting = bundle.ShopSetting ?? new ShopSetting { Name = bundle.ShopName ?? "" };
        setting.Id = 0;
        setting.ShopId = shopId;
        if (syncedRemoteModifiedTime is not null)
            setting.GoogleDriveLastSyncedRemoteModifiedTime = syncedRemoteModifiedTime;
        db.ShopSettings.Add(setting);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// 把一批實體的 Id 清空讓資料庫重新配發，插入後回傳「舊 Id → 新 Id」對照表。
    /// <paramref name="prepare"/> 在清空 Id 之後、插入之前執行，用來設定 ShopId／改寫外鍵。
    /// </summary>
    private async Task<Dictionary<int, int>> InsertWithNewIdsAsync<T>(
        List<T> items, Action<T> prepare, CancellationToken ct) where T : class
    {
        var map = new Dictionary<int, int>();
        if (items.Count == 0) return map;

        var idProperty = typeof(T).GetProperty("Id")
            ?? throw new InvalidOperationException($"{typeof(T).Name} 沒有 Id 屬性，無法重新配發。");
        var oldIds = items.Select(i => (int)idProperty.GetValue(i)!).ToList();

        foreach (var item in items)
        {
            idProperty.SetValue(item, 0);
            prepare(item);
        }

        db.Set<T>().AddRange(items);
        await db.SaveChangesAsync(ct);

        for (int i = 0; i < items.Count; i++)
            map[oldIds[i]] = (int)idProperty.GetValue(items[i])!;

        // 新 Id 都抄進對照表了，這批實體沒有人再需要——從追蹤器移除，避免資料量大時
        // 追蹤器一路累積到整份備份的所有實體。這時交易還沒 commit，Clear 不影響已寫入的內容。
        db.ChangeTracker.Clear();

        return map;
    }

    private static int? RemapNullable(Dictionary<int, int> map, int? oldId) =>
        oldId is null ? null : map.GetValueOrDefault(oldId.Value, oldId.Value);

    private static List<int> RemapList(Dictionary<int, int> map, List<int> oldIds) =>
        oldIds.Select(id => map.GetValueOrDefault(id, id)).ToList();
}
