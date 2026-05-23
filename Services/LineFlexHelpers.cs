namespace ShopManager.Services;

/// <summary>
/// LINE Flex Message 共用元件建構器（回傳 anonymous object 樹，
/// 由 <see cref="LineService.PushFlexMessageAsync"/> 統一序列化）。
/// </summary>
internal static class LineFlexHelpers
{
    /// <summary>水平兩欄：左標籤、右內容</summary>
    public static object Row(string label, string value, string labelColor = "#888888", string valueColor = "#222222",
        string size = "sm", string? valueAlign = null, int labelFlex = 3, int valueFlex = 5)
    {
        return new
        {
            type   = "box",
            layout = "horizontal",
            contents = new object[]
            {
                new { type = "text", text = label, color = labelColor, flex = labelFlex, size },
                new { type = "text", text = value, color = valueColor, flex = valueFlex, size, align = valueAlign ?? "start", wrap = true },
            }
        };
    }

    /// <summary>粗體強調列（如「應領薪資」）</summary>
    public static object RowEmphasis(string label, string value, string valueColor = "#4A90D9")
    {
        return new
        {
            type   = "box",
            layout = "horizontal",
            contents = new object[]
            {
                new { type = "text", text = label, weight = "bold", flex = 3, size = "md", color = "#222222" },
                new { type = "text", text = value, weight = "bold", flex = 5, size = "md", color = valueColor, align = "end" },
            }
        };
    }

    public static object Separator(string margin = "md")
        => new { type = "separator", margin };

    /// <summary>標準頁首：副標 + 主標題，深色底</summary>
    public static object Header(string subtitle, string title, string backgroundColor = "#4A90D9")
    {
        return new
        {
            type            = "box",
            layout          = "vertical",
            backgroundColor,
            paddingAll      = "lg",
            contents = new object[]
            {
                new { type = "text", text = subtitle, color = "#FFFFFFB0", size = "xs" },
                new { type = "text", text = title,    color = "#FFFFFF",   weight = "bold", size = "lg", margin = "xs" },
            }
        };
    }

    /// <summary>將 header + body contents 包成 bubble</summary>
    public static object Bubble(object header, IList<object> bodyContents, string bodySpacing = "sm")
    {
        return new
        {
            type   = "bubble",
            size   = "kilo",
            header,
            body   = new
            {
                type       = "box",
                layout     = "vertical",
                spacing    = bodySpacing,
                paddingAll = "lg",
                contents   = bodyContents.ToArray(),
            }
        };
    }
}
