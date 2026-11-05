using ShopManager.Data;
using System.IO;
using System.Text.Json;

namespace ShopManager.Services;

/// <summary>已保存的某月打卡檔資訊</summary>
public record StoredAttendanceFile(string OriginalFileName, DateTime ImportedAt, string FilePath);

/// <summary>
/// 匯入的 POS 打卡檔另存一份在資料庫旁（Attendance\{ShopId}\yyyy-MM.xlsx），重新計算同月薪資時自動沿用。
/// 依店鋪分資料夾：所有店鋪共用同一個資料庫目錄，同月份檔名會撞在一起。
/// 每月只留一份，再次匯入即覆蓋；原始檔名與匯入時間記在同名 .json。
/// </summary>
public static class AttendanceFileStore
{
    private sealed record Meta(string OriginalFileName, DateTime ImportedAt);

    private static string ShopDir(Guid shopId) => Path.Combine(
        Path.GetDirectoryName(AppDbContext.DefaultDbPath())!, "Attendance", shopId.ToString("N"));

    private static string DataPath(Guid shopId, int year, int month) =>
        Path.Combine(ShopDir(shopId), $"{year:D4}-{month:D2}.xlsx");

    private static string MetaPath(Guid shopId, int year, int month) =>
        Path.ChangeExtension(DataPath(shopId, year, month), ".json");

    public static StoredAttendanceFile? Get(Guid shopId, int year, int month)
    {
        var data = DataPath(shopId, year, month);
        if (!File.Exists(data)) return null;

        Meta? meta = null;
        try { meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(MetaPath(shopId, year, month))); }
        catch { /* 中繼檔遺失或損壞：退回用檔案本身的資訊 */ }

        return new StoredAttendanceFile(
            meta?.OriginalFileName ?? Path.GetFileName(data),
            meta?.ImportedAt ?? File.GetLastWriteTime(data),
            data);
    }

    /// <summary>覆蓋該月的打卡檔（先寫暫存檔再取代，寫到一半失敗不會毀掉舊檔）</summary>
    public static void Save(Guid shopId, int year, int month, byte[] content, string originalFileName, DateTime importedAt)
    {
        Directory.CreateDirectory(ShopDir(shopId));
        var data = DataPath(shopId, year, month);
        var tmp  = data + ".tmp";
        File.WriteAllBytes(tmp, content);
        File.Move(tmp, data, overwrite: true);
        File.WriteAllText(MetaPath(shopId, year, month),
            JsonSerializer.Serialize(new Meta(originalFileName, importedAt)));
    }

    public static void Delete(Guid shopId, int year, int month)
    {
        File.Delete(DataPath(shopId, year, month));
        File.Delete(MetaPath(shopId, year, month));
    }

    /// <summary>關閉店鋪時一併清掉該店所有打卡檔</summary>
    public static void DeleteShop(Guid shopId)
    {
        var dir = ShopDir(shopId);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
