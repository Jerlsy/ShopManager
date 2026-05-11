using ShopManager.Models;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ShopManager.Services;

public class BankCodeService(HttpClient http)
{
    private static readonly string _updateUrl =
        "https://raw.githubusercontent.com/nczz/taiwan-banks-list/master/data/bank.json";

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
            var raw = JsonSerializer.Deserialize<List<BankCodeDto>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (raw is null || raw.Count == 0)
                return (false, "回傳資料為空", 0);

            _cache = raw
                .Where(r => !string.IsNullOrWhiteSpace(r.Code) && !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => new BankCode(r.Code!.Trim(), r.Name!.Trim()))
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

    private class BankCodeDto
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
    }
}
