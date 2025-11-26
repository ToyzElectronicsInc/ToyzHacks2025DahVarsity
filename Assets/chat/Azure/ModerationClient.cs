// ModerationClient.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

public enum ModerationAction { Allow, Warn, Redact, Block, Escalate }

public class ModerationResult
{
    public ModerationAction Action { get; set; } = ModerationAction.Allow;
    public double OverallScore { get; set; } = 0.0;
    public Dictionary<string, (string label, double score)> Categories { get; set; } = new();
    public string Explanation { get; set; } = "";
    public string[] Evidence { get; set; } = Array.Empty<string>();
}

public class ModerationClient : MonoBehaviour
{
    [Tooltip("The moderation service endpoint (e.g. https://moderation.yourdomain.com/api/moderate/fast)")]
    public string ModerationServerUrl = "https://your-moderation.example.com/api/moderate/fast";

    private HttpClient _http;

    void Awake()
    {
        _http = new HttpClient();

        if (ModerationTransport == null)
        {
            ModerationTransport = FindObjectOfType<ModerationClient>();
            if (ModerationTransport != null)
                Debug.Log("[ChatModerationClient] ModerationTransport found and wired automatically.");
        }
    }

    public async Task<ModerationResult> FastModerateAsync(string text, string roomId = null, string senderId = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return new ModerationResult();

        try
        {
            var payload = new { text = text, roomId = roomId, senderId = senderId };
            var json = JsonSerializer.Serialize(payload);
            var resp = await _http.PostAsync(ModerationServerUrl, new StringContent(json, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
            {
                Debug.LogWarning($"[ModerationClient] server returned {resp.StatusCode}");
                return new ModerationResult { Action = ModerationAction.Warn, Explanation = "Moderation service unavailable" };
            }
            var body = await resp.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var mapped = JsonSerializer.Deserialize<ModerationResultDto>(body, options);
            if (mapped == null) return new ModerationResult { Action = ModerationAction.Warn };

            return MapDto(mapped);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ModerationClient] error: " + ex.Message);
            return new ModerationResult { Action = ModerationAction.Warn, Explanation = "Moderation call failed" };
        }
    }

    // DTO matches the server response shape (adjust if your server uses different names)
    private class ModerationResultDto
    {
        public string Action { get; set; }
        public double OverallScore { get; set; }
        public Dictionary<string, CategoryDto> Categories { get; set; }
        public string Explanation { get; set; }
        public string[] Evidence { get; set; }
    }
    private class CategoryDto { public string Label { get; set; } public double Score { get; set; } }

    private ModerationResult MapDto(ModerationResultDto dto)
    {
        var outp = new ModerationResult();
        if (Enum.TryParse<ModerationAction>(dto.Action, true, out var act)) outp.Action = act;
        outp.OverallScore = dto.OverallScore;
        if (dto.Categories != null)
        {
            foreach (var k in dto.Categories.Keys)
            {
                var c = dto.Categories[k];
                outp.Categories[k] = (c.Label ?? "none", c.Score);
            }
        }
        outp.Explanation = dto.Explanation ?? "";
        outp.Evidence = dto.Evidence ?? Array.Empty<string>();
        return outp;
    }
}