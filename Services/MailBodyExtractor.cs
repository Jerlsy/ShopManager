using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ShopManager.Services;

/// <summary>
/// 從郵件正文擷取通知內容。
///
/// 設計取捨：不同寄件者的信件版面天差地遠，「自動判斷哪幾行才是重點」沒有可靠的通用解
/// （試過用頁尾關鍵字截斷，但行銷文案常夾在有用內容與頁尾之間，猜不準）。因此拆成兩層：
///
/// • 通用層：HTML → 純文字、去轉寄標頭／圖片佔位／網址、壓縮空白。這部分對任何信都成立。
/// • 規則層：由使用者為每條規則指定「欄位名稱」清單（存入金額、授權金額…），
///   本類別負責把每個欄位後面的值抓出來。通知信的欄位名稱在同一個寄件者間是固定的，
///   所以這比任何啟發式猜測都可靠，輸出也是結構化的「欄位：值」而非整行原文。
///
/// ⚠️ 規則需與 <see cref="GmailAppsScriptTemplate"/> 內的 JS 版本保持一致：設定頁的「測試」
///    用這支預覽，實際推播跑的是 Apps Script 那支，改動請兩邊一起改。
/// </summary>
public static class MailBodyExtractor
{
    // ── 通用清理 ─────────────────────────────────────────────────────────────
    private static readonly Regex ScriptStyleHead = new(
        @"<(script|style|head)[^>]*>.*?</\1>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 視覺上隱藏的元素（font-size:0、display:none…）連同內容一起丟掉。
    ///
    /// 這不是特例處理：銀行會在數值中間插 <c>&lt;span style="font-size:0px"&gt;/&lt;/span&gt;</c>
    /// 這類隱形字元來干擾自動解析（中國信託的存入帳號就是），行銷信也常用 display:none 藏預覽文字。
    /// 這些內容使用者根本看不到，本來就不該出現在擷取結果裡。
    /// </summary>
    private static readonly Regex HiddenElement = new(
        @"<(\w+)[^>]*style\s*=\s*(?<q>[""'])[^""']*(?:display\s*:\s*none|visibility\s*:\s*hidden|font-size\s*:\s*0(?![.1-9])|mso-hide\s*:\s*all|opacity\s*:\s*0(?![.1-9]))[^""']*\k<q>[^>]*>.*?</\1>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LineBreakTag = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BlockCloseTag = new(@"</(p|div|tr|table|h[1-6]|li)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CellCloseTag  = new(@"</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTag        = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex SourceWhitespace = new(@"\s+", RegexOptions.Compiled);

    // 轉寄標頭改逐行判斷，不用「找到空行為止」——郵件的換行實務上很亂（\r\n、\r\r\n、
    // 欄位之間夾空行都有），拿空行當界線會在第一個換行就誤判結束。
    private static readonly Regex ForwardSeparator = new(
        @"^-{3,}\s*(轉寄的郵件|轉寄郵件|Forwarded message|原始郵件)\s*-{3,}$",
        RegexOptions.Compiled);
    private static readonly Regex ForwardHeaderField = new(
        @"^(寄件者|日期|主旨|收件者|副本|密件副本|From|Sent|Date|Subject|To|Cc|Bcc)\s*[:：]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ImagePlaceholder = new(@"\[image:[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex BracketedUrl     = new(@"<https?://[^>]*>", RegexOptions.Compiled);
    private static readonly Regex BareUrl          = new(@"https?://\S+", RegexOptions.Compiled);
    private static readonly Regex Whitespace       = new(@"[ \t 　]+", RegexOptions.Compiled);

    // 欄位名稱與值之間、以及值尾端可能殘留的分隔符
    private static readonly Regex LeadSeparator  = new(@"^[\s:：．・\-—–|｜=＝]+", RegexOptions.Compiled);
    private static readonly Regex TrailSeparator = new(@"[\s:：．・\-—–|｜]+$", RegexOptions.Compiled);

    /// <summary>HTML 轉純文字。純 HTML 信（例如富邦的消費通知）沒有 text/plain 段落時要靠這個。</summary>
    public static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var text = ScriptStyleHead.Replace(html, "");
        text = HiddenElement.Replace(text, "");

        // HTML 原始碼裡的換行／縮排只是排版空白，先全部壓成空格。少了這一步，像
        // 「<td>存入帳號</td>\n<td>822-…</td>」這種常見寫法會被拆成兩行，標籤與值就對不起來了；
        // 真正該換行的地方由下面的區塊標籤決定。
        text = SourceWhitespace.Replace(text, " ");

        text = LineBreakTag.Replace(text, "\n");
        text = BlockCloseTag.Replace(text, "\n");
        text = CellCloseTag.Replace(text, "\t");   // 儲存格間用 tab，避免標籤與值黏在一起
        text = AnyTag.Replace(text, "");
        return WebUtility.HtmlDecode(text);
    }

    /// <summary>整理成乾淨的行陣列（去轉寄標頭／圖片佔位／網址，壓縮空白，丟掉空行）</summary>
    public static List<string> ToLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = ImagePlaceholder.Replace(text, "");
        text = BracketedUrl.Replace(text, "");
        text = BareUrl.Replace(text, "");

        var lines = new List<string>();
        var inForwardHeader = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = Whitespace.Replace(raw, " ").Trim();

            if (ForwardSeparator.IsMatch(line)) { inForwardHeader = true; continue; }
            if (inForwardHeader)
            {
                if (line.Length == 0) continue;                       // 標頭欄位之間的空行
                if (ForwardHeaderField.IsMatch(line)) continue;       // 寄件者／日期／主旨／收件者…
                inForwardHeader = false;                              // 第一行真正的內容，標頭結束
            }

            if (line.Length > 1) lines.Add(line);   // 順便濾掉移除連結後殘留的單一標點
        }
        return lines;
    }

    /// <summary>推播卡片上的一列：標題列只有文字，欄位列是「標籤＋值」</summary>
    public record ExtractedRow(string Label, string Value, bool IsHeading);

    /// <summary>
    /// 主要進入點：把正文轉成要推播的文字（設定頁的測試預覽用；實際卡片版面由 Apps Script 排）。
    /// </summary>
    public static string Extract(string body, IReadOnlyList<string>? fields = null, int maxLength = 900) =>
        Extract(body, fields, out _, maxLength);

    /// <inheritdoc cref="Extract(string, IReadOnlyList{string}?, int)"/>
    /// <param name="missing">抓不到值的欄位名稱，供設定頁的測試提醒使用者</param>
    public static string Extract(string body, IReadOnlyList<string>? fields,
        out List<string> missing, int maxLength = 900)
    {
        var rows = ExtractRows(body, fields, out missing);
        var result = string.Join("\n", rows.Select(r => r.IsHeading ? r.Value : $"{r.Label}：{r.Value}"));
        return result.Length > maxLength ? result[..maxLength] + "…" : result;
    }

    /// <summary>
    /// 依樣板產生卡片列。樣板每一行是：
    /// • <c>[文字]</c>  → 字面文字，不進比對，原樣顯示在這個順序位置（當標題／分隔說明用）
    /// • 其他           → 欄位名稱，去信件內文找它後面的值
    ///
    /// 樣板留空、或一個欄位都沒抓到值時，退回清理後的全文——寧可推出多一點內容，也不要推出空訊息。
    /// </summary>
    public static List<ExtractedRow> ExtractRows(
        string body, IReadOnlyList<string>? template, out List<string> missing)
    {
        missing = [];
        var lines = ToLines(body);

        if (template is not { Count: > 0 })
            return [new ExtractedRow("", string.Join("\n", lines), true)];

        // 只有「真正要比對的欄位名稱」才參與「同一行的下一個欄位」切斷判斷，字面文字不算
        var fieldNames = template.Where(t => !IsLiteral(t)).Select(StripBrackets).ToList();

        var rows = new List<ExtractedRow>();
        foreach (var entry in template)
        {
            if (IsLiteral(entry))
            {
                var text = StripBrackets(entry);
                if (text.Length > 0) rows.Add(new ExtractedRow("", text, true));
                continue;
            }

            var value = FindValue(lines, entry, fieldNames);
            if (value is not null) rows.Add(new ExtractedRow(entry, value, false));
            else missing.Add(entry);
        }

        // 一個欄位都沒抓到（只剩標題文字）就退回全文，避免推出一張沒有內容的卡片
        if (!rows.Any(r => !r.IsHeading))
            rows.Add(new ExtractedRow("", string.Join("\n", lines), true));

        return rows;
    }

    private static bool IsLiteral(string entry)
    {
        var t = entry.Trim();
        return t.Length >= 2 && t[0] == '[' && t[^1] == ']';
    }

    private static string StripBrackets(string entry)
    {
        var t = entry.Trim();
        return IsLiteral(t) ? t[1..^1].Trim() : t;
    }

    /// <summary>單一欄位值的長度上限，避免欄位名稱不小心命中一整段內文時吃掉整張卡片</summary>
    private const int MaxValueLength = 200;

    /// <summary>在內文裡找出這個欄位名稱後面的值；找不到回傳 null</summary>
    private static string? FindValue(
        IReadOnlyList<string> lines, string label, IReadOnlyList<string> allFieldNames)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var pos = lines[i].IndexOf(label, StringComparison.Ordinal);
            if (pos < 0) continue;

            var afterLabel = lines[i][(pos + label.Length)..];
            var rest = LeadSeparator.Replace(afterLabel, "");

            // 同一行還有其他欄位名稱時，值只取到下一個欄位之前
            var cut = rest.Length;
            foreach (var other in allFieldNames)
            {
                if (other == label) continue;
                var p = rest.IndexOf(other, StringComparison.Ordinal);
                if (p >= 0) cut = Math.Min(cut, p);
            }
            rest = rest[..cut];

            // 值被排版拆到下一行（標籤與值分屬不同 <tr>）才往下抓。
            // 條件是「標籤後面原本有分隔符、只是分隔符後面沒東西」——像「存入金額：」這樣。
            // 若標籤後面根本沒有分隔符，那多半是個標題行（例如整行就是「存款入帳通知」），
            // 硬抓下一行只會拿到不相干的內文。
            var hadSeparator = afterLabel.Trim().Length > 0;
            if (rest.Trim().Length == 0 && hadSeparator && i + 1 < lines.Count)
                rest = lines[i + 1];

            var candidate = TrailSeparator.Replace(rest.Trim(), "");
            if (candidate.Length == 0) continue;   // 這次命中沒有值，繼續往後找下一個出現的位置

            return candidate.Length > MaxValueLength ? candidate[..MaxValueLength] + "…" : candidate;
        }
        return null;
    }

    /// <summary>Gmail API 的 base64url 內容解碼</summary>
    public static string DecodeBase64Url(string data)
    {
        var s = data.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
