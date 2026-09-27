using ShopManager.Models;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace ShopManager.Services;

/// <summary>
/// 解析 POS 匯出的打卡紀錄 xlsx。格式：A 欄為人名列，之後每列「上班/下班 | 時間 | 時數」，
/// 夾雜「無上班記錄／無下班記錄」與「總時數：…」彙總列。xlsx 本質是 zip＋XML，直接讀免裝套件。
/// </summary>
public static class AttendanceImportService
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // 同一天所有打卡都擠在這個時間內，視為只打了一次卡（例如按錯鍵後立刻補按）
    private static readonly TimeSpan MinWorkSpan = TimeSpan.FromMinutes(5);

    public static List<ClockPerson> Parse(string path)
    {
        using var zip = ZipFile.OpenRead(path);

        var shared = new List<string>();
        var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (ssEntry is not null)
        {
            using var s = ssEntry.Open();
            shared = XDocument.Load(s).Root!.Elements(Ns + "si")
                .Select(si => string.Concat(si.Descendants(Ns + "t").Select(t => t.Value)))
                .ToList();
        }

        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                                             && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("檔案中找不到工作表");

        XDocument sheet;
        using (var s = sheetEntry.Open()) sheet = XDocument.Load(s);

        var people = new List<ClockPerson>();
        ClockPerson? current = null;

        foreach (var row in sheet.Descendants(Ns + "row"))
        {
            var cells = row.Elements(Ns + "c").ToDictionary(
                c => new string(((string?)c.Attribute("r") ?? "").TakeWhile(char.IsLetter).ToArray()),
                c => CellText(c, shared));

            var a = cells.GetValueOrDefault("A")?.Trim() ?? string.Empty;
            var b = cells.GetValueOrDefault("B")?.Trim() ?? string.Empty;
            if (a.Length == 0) continue;

            if (a is "上班" or "下班")
            {
                if (current is not null && TryParseTime(b, out var t))
                    current.Punches.Add(new ClockPunch(t, a == "上班"));
                continue;
            }
            if (a.StartsWith("無上班") || a.StartsWith("無下班") || a.StartsWith("總時數")) continue;

            current = new ClockPerson { Name = a };
            people.Add(current);
        }

        return people.Where(p => p.Punches.Count > 0).ToList();
    }

    /// <summary>
    /// 依日期彙整：當天最早一筆＝上班、最晚一筆＝下班。
    /// 這樣「忘了打下班、收班時補按上班＋下班」與「先誤按下班再按上班」都能還原成正確的一段。
    /// </summary>
    public static Dictionary<DateOnly, DayPunch> BuildDays(IEnumerable<ClockPunch> punches)
    {
        var result = new Dictionary<DateOnly, DayPunch>();
        foreach (var g in punches.GroupBy(p => DateOnly.FromDateTime(p.Time)))
        {
            var sorted = g.OrderBy(p => p.Time).ToList();
            var first  = sorted[0];
            var last   = sorted[^1];

            if (last.Time - first.Time >= MinWorkSpan)
                result[g.Key] = new DayPunch { In = first.Time, Out = last.Time };
            else if (sorted.All(p => !p.IsClockIn))
                result[g.Key] = new DayPunch { Out = last.Time };
            else
                result[g.Key] = new DayPunch { In = first.Time };
        }
        return result;
    }

    private static string CellText(XElement c, List<string> shared)
    {
        var type = (string?)c.Attribute("t");
        if (type == "inlineStr")
            return string.Concat(c.Descendants(Ns + "t").Select(t => t.Value));

        var v = c.Element(Ns + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(v, out var idx) && idx >= 0 && idx < shared.Count)
            return shared[idx];
        return v;
    }

    private static bool TryParseTime(string text, out DateTime time)
    {
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            return true;
        // 日期格式儲存格（Excel 序號）
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa) && oa > 1)
        {
            time = DateTime.FromOADate(oa);
            return true;
        }
        return false;
    }
}
