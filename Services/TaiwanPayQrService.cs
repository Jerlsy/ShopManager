using QRCoder;
using System.IO;
using System.Windows.Media.Imaging;

namespace ShopManager.Services;

/// <summary>
/// 產生「TWQR 共通支付規範」之個人轉帳 QR Code Payload。
///
/// 格式為 URI 而非 EMV TLV：
///   TWQRP://&lt;說明&gt;/158/02/V1?D1=&lt;金額×100&gt;&D5=&lt;銀行代碼3位&gt;&D6=&lt;帳號補零16位&gt;[&D9=&lt;備註&gt;]
///
/// 路徑欄位：
///   158  銀行業別代碼（固定）
///   02   業務類別（02 = 個人轉帳）
///   V1   規範版本
///
/// 參數：
///   D1   金額（最多 7 位整數，後綴 "00" 表小數兩位零）；省略代表掃描時手動輸入
///   D5   轉入銀行代碼（3 碼）
///   D6   轉入銀行帳號（補零至 16 碼）
///   D9   附言欄（可選）
///
/// D 類參數轉帳時無法被付款人修改，適合薪資轉帳場景。
/// </summary>
public static class TaiwanPayQrService
{
    /// <summary>建構 TWQRP URI</summary>
    public static string BuildPayload(string bankCode, string account, decimal? amount = null, string? memo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bankCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(account);

        var paddedBank    = bankCode.PadLeft(3, '0');
        var paddedAccount = account.PadLeft(16, '0');

        // 路徑開頭的說明欄使用 URL-safe ASCII，避免部分掃描器對 UTF-8 路徑處理不一致
        var sb = new System.Text.StringBuilder($"TWQRP://salary/158/02/V1?D5={paddedBank}&D6={paddedAccount}");

        if (amount.HasValue && amount.Value > 0)
        {
            // D1：金額 × 100（補小數兩位零）
            long cents = (long)Math.Round(amount.Value * 100m);
            sb.Append($"&D1={cents}");
        }

        if (!string.IsNullOrWhiteSpace(memo))
            sb.Append($"&D9={Uri.EscapeDataString(memo)}");

        return sb.ToString();
    }

    /// <summary>產生 QR Code PNG bytes</summary>
    public static byte[] BuildPng(string payload, int pixelsPerModule = 8)
    {
        using var gen  = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var qr   = new PngByteQRCode(data);
        return qr.GetGraphic(pixelsPerModule);
    }

    /// <summary>產生 BitmapImage 供 WPF Image.Source 直接綁定</summary>
    public static BitmapImage BuildBitmap(string payload, int pixelsPerModule = 8)
    {
        var pngBytes = BuildPng(payload, pixelsPerModule);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(pngBytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
