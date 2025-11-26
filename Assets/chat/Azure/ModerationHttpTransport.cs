// ModerationHttpTransport.cs
using System.Net.Http;
using System.Text;
using System.Text.Json;

// IModerationTransport.cs
public interface IModerationTransport
{
    Task<ModerationResult> FastModerateAsync(string text, string roomId = null, string senderId = null);
}

public class ModerationHttpTransport : IModerationTransport
{
    private readonly HttpClient _http;
    private readonly string _endpoint;

    public ModerationHttpTransport(HttpClient http, string endpoint)
    {
        _http = http;
        _endpoint = endpoint;
    }

    public async Task<ModerationResult> FastModerateAsync(string text, string roomId = null, string senderId = null)
    {
        var req = new { text, roomId, senderId };
        var json = JsonSerializer.Serialize(req);
        using var resp = await _http.PostAsync(_endpoint, new StringContent(json, Encoding.UTF8, "application/json"));
        if (!resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ModerationResult>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}
