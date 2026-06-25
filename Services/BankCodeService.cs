using ShopManager.Models;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShopManager.Services;

public class BankCodeService(HttpClient http)
{
    // 來源改版：原 data/bank.json 已移除，改用依分類巢狀整理的 banks_sort_by_cats.json（main 分支）
    private static readonly string _updateUrl =
        "https://raw.githubusercontent.com/nczz/taiwan-banks-list/main/banks_sort_by_cats.json";

    private List<BankCode>? _cache;

    public Task<List<BankCode>> GetAllAsync()
    {
        if (_cache is not null) return Task.FromResult(_cache);
        _cache = LoadEmbedded();
        return Task.FromResult(_cache);
    }

    public async Task<(bool Success, string Message, int Count)> UpdateFromWebAsync()
    {
        try
        {
            var json = await http.GetStringAsync(_updateUrl);
            var categories = JsonSerializer.Deserialize<Dictionary<string, List<WebBankCodeDto>>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var raw = categories?.Values.SelectMany(v => v).ToList();
            if (raw is null || raw.Count == 0)
                return (false, "回傳資料為空", 0);

            _cache = raw
                .Where(r => !string.IsNullOrWhiteSpace(r.Code) && !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => new BankCode(r.Code!.Trim(), r.Name!.Trim()))
                .DistinctBy(b => b.Code)
                .OrderBy(b => b.Code)
                .ToList();

            return (true, $"已更新 {_cache.Count} 筆銀行代碼", _cache.Count);
        }
        catch (Exception ex)
        {
            return (false, $"更新失敗：{ex.Message}", 0);
        }
    }

    private static List<BankCode> LoadEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("ShopManager.Resources.banks.json");
        if (stream is null) return new();
        var raw = JsonSerializer.Deserialize<List<BankCodeDto>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return raw?
            .Where(r => !string.IsNullOrWhiteSpace(r.Code) && !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new BankCode(r.Code!.Trim(), r.Name!.Trim()))
            .OrderBy(b => b.Code)
            .ToList() ?? new();
    }

    // 內嵌資源（Resources/banks.json）格式：{"code":"...","name":"..."}
    private class BankCodeDto
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
    }

    // 線上來源（banks_sort_by_cats.json）格式：{"bank_code":"...","name":"..."}
    private class WebBankCodeDto
    {
        [JsonPropertyName("bank_code")]
        public string? Code { get; set; }
        public string? Name { get; set; }
    }
}
