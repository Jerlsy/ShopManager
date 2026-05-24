using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ShopManager.Services;

public class LineService
{
    private readonly HttpClient _http;

    public LineService(HttpClient http) => _http = http;

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(string token)
    {
        try
        {
            using var req = CreateAuthorizedRequest(HttpMethod.Get, "https://api.line.me/v2/bot/info", token);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return (false, $"驗證失敗（HTTP {(int)res.StatusCode}），請確認 Token 是否正確");

            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            var botName = json.GetProperty("displayName").GetString() ?? "未知";
            return (true, $"連線成功：{botName}");
        }
        catch (Exception ex)
        {
            return (false, $"連線錯誤：{ex.Message}");
        }
    }

    public async Task<List<(string UserId, string DisplayName, string? PictureUrl)>> GetFollowersFromWorkerAsync(string workerUrl, string apiKey)
    {
        var url = workerUrl.TrimEnd('/') + "/followers";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Api-Key", apiKey);
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            throw new HttpRequestException($"取得好友清單失敗（HTTP {(int)res.StatusCode}）：{body}");
        }
        var items = await res.Content.ReadFromJsonAsync<JsonElement[]>();
        var result = new List<(string, string, string?)>();
        if (items == null) return result;
        foreach (var item in items)
        {
            var userId      = item.TryGetProperty("userId",      out var u)  ? u.GetString()  ?? string.Empty : string.Empty;
            var displayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "未知"        : "未知";
            var pictureUrl  = item.TryGetProperty("pictureUrl",  out var pu) ? pu.GetString() : null;
            if (!string.IsNullOrEmpty(userId))
                result.Add((userId, displayName, pictureUrl));
        }
        return result;
    }

    public async Task<List<string>> GetFollowerIdsAsync(string token)
    {
        var ids = new List<string>();
        string? next = null;
        do
        {
            var url = "https://api.line.me/v2/bot/followers/ids?limit=1000" + (next != null ? $"&start={next}" : "");
            using var req = CreateAuthorizedRequest(HttpMethod.Get, url, token);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                throw new HttpRequestException($"取得好友清單失敗（HTTP {(int)res.StatusCode}）：{body}");
            }
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("userIds", out var arr))
                foreach (var id in arr.EnumerateArray())
                    ids.Add(id.GetString()!);
            next = json.TryGetProperty("next", out var n) ? n.GetString() : null;
        } while (next != null);
        return ids;
    }

    public async Task<(string DisplayName, string? PictureUrl)> GetProfileAsync(string token, string userId)
    {
        try
        {
            using var req = CreateAuthorizedRequest(HttpMethod.Get, $"https://api.line.me/v2/bot/profile/{userId}", token);
            var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return ("未知", null);
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            return (
                json.TryGetProperty("displayName", out var n) ? n.GetString() ?? "未知" : "未知",
                json.TryGetProperty("pictureUrl",  out var p) ? p.GetString() : null
            );
        }
        catch { return ("未知", null); }
    }

    public async Task<(string Url, string Key)?> UploadScheduleImageAsync(
        string workerUrl, string apiKey, byte[] pngBytes)
    {
        var url = workerUrl.TrimEnd('/') + "/upload-image";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-Api-Key", apiKey);
        req.Content = new ByteArrayContent(pngBytes);
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var res = await _http.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;
        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return (json.GetProperty("url").GetString()!, json.GetProperty("key").GetString()!);
    }

    public async Task<bool> PushImageAsync(string token, string userId, string imageUrl)
    {
        var body = JsonSerializer.Serialize(new 
        {
            to       = userId,
            messages = new[] { new { type = "image", originalContentUrl = imageUrl, previewImageUrl = imageUrl } }
        });
        using var req = CreateAuthorizedRequest(HttpMethod.Post, "https://api.line.me/v2/bot/message/push", token);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var res = await _http.SendAsync(req);
        return res.IsSuccessStatusCode;
    }

    public async Task DeleteScheduleImageAsync(string workerUrl, string apiKey, string key)
    {
        var url = workerUrl.TrimEnd('/') + $"/delete-image?key={Uri.EscapeDataString(key)}";
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Add("X-Api-Key", apiKey);
        await _http.SendAsync(req);
    }

    public async Task<bool> PushMessageAsync(string token, string userId, string text)
    {
        var body = JsonSerializer.Serialize(new
        {
            to       = userId,
            messages = new[] { new { type = "text", text } }
        });

        using var req = CreateAuthorizedRequest(HttpMethod.Post, "https://api.line.me/v2/bot/message/push", token);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var res = await _http.SendAsync(req);
        return res.IsSuccessStatusCode;
    }

    /// <summary>
    /// 推播 Flex Message（LINE 的卡片式訊息，等同 Teams Adaptive Card）。
    /// contents 為 bubble / carousel 結構的物件（會以 JsonSerializer 序列化）。
    /// </summary>
    public async Task<bool> PushFlexMessageAsync(string token, string userId, string altText, object contents)
    {
        // altText 最長 400 字
        if (altText.Length > 400) altText = altText[..400];

        var body = JsonSerializer.Serialize(new
        {
            to       = userId,
            messages = new object[]
            {
                new
                {
                    type     = "flex",
                    altText  = altText,
                    contents = contents,
                }
            }
        });

        using var req = CreateAuthorizedRequest(HttpMethod.Post, "https://api.line.me/v2/bot/message/push", token);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var res = await _http.SendAsync(req);
        return res.IsSuccessStatusCode;
    }
}
