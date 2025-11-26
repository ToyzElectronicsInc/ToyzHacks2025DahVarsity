/*using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

public class ChatModerationClient : MonoBehaviour
{
    [Header("Server")]
    public string ModerationServerUrl = "https://yourserver.example.com/api/moderate/fast"; // replace

    // at top of ChatModerationClient.cs (inside class) add:
    [Header("Local moderation transport (optional)")]
    [Tooltip("Optional: if assigned, ChatModerationClient will use this ModerationClient to call the server. " +
            "Otherwise it falls back to its internal HTTP call.")]
    public ModerationClient ModerationTransport; // assign in inspector or auto-find

    // then modify CallModerationServer(...) to prefer ModerationTransport:
    private async Task<ModerationResult> CallModerationServer(string text)
    {
        try
        {
            // prefer using the ModerationClient transport if it exists
            if (ModerationTransport != null)
            {
                return await ModerationTransport.FastModerateAsync(text,
                        roomId: Photon.Pun.PhotonNetwork.CurrentRoom?.Name,
                        senderId: Photon.Pun.PhotonNetwork.LocalPlayer?.UserId);
            }

            // existing HTTP implementation (fallback)
            var req = new { text = text, roomId = Photon.Pun.PhotonNetwork.CurrentRoom?.Name, senderId = Photon.Pun.PhotonNetwork.LocalPlayer.UserId };
            var json = JsonSerializer.Serialize(req);
            var resp = await _http.PostAsync(ModerationServerUrl, new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Debug.LogWarning("[ChatModerationClient] moderation server returned error: " + resp.StatusCode);
                return null;
            }
            var mod = JsonSerializer.Deserialize<ModerationResult>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return mod;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ChatModerationClient] moderation call failed: " + ex);
            return null;
        }
    }

    public bool UseMockModeration = false; // set true to test without server

    private HttpClient _http;

    private void Awake()
    {
        _http = new HttpClient();
        if (ModerationTransport == null) ModerationTransport = new ModerationHttpTransport(_http, ModerationServerUrl);
    }

    // Call this from your transcript handler
    public async Task<ChatMessagePayload> HandleVoiceTranscriptAsync(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;

        // 1) Local fast checks
        var quick = LocalQuickFilter.Check(transcript);
        if (quick == LocalQuickFilter.Result.Block)
        {
            ShowUserMessage("Your message contains disallowed content and was blocked.");
            return;
        }
        else if (quick == LocalQuickFilter.Result.Warn)
        {
            bool ok = await ShowWarningAndConfirm("Your message may violate community guidelines. Send anyway?");
            if (!ok) return;
        }

        // 2) Server fast moderation
        ModerationResult mod;
        if (UseMockModeration) mod = MockModeration(transcript); // local simulated
        else if (ModerationTransport != null) mod = await ModerationTransport.FastModerateAsync(transcript, PhotonNetwork.CurrentRoom?.Name, PhotonNetwork.LocalPlayer?.UserId);
        else mod = await CallModerationServer(transcript);

        if (mod == null)
        {
            // conservative fallback: warn and allow after confirmation
            bool ok = await ShowWarningAndConfirm("Moderation unavailable. Send anyway?");
            if (!ok) return;
        }
        else
        {
            switch (mod.Action)
            {
                case ModerationAction.Block:
                    ShowUserMessage("Message blocked for policy violation.");
                    // optionally log incident to server
                    return;

                case ModerationAction.Warn:
                    {
                        bool ok = await ShowWarningAndConfirm($"Possible violation: {mod.Explanation}. Send anyway?");
                        if (!ok) return;
                        break;
                    }
                case ModerationAction.Redact:
                    transcript = RedactSensitiveParts(transcript, mod.Evidence);
                    break;
                case ModerationAction.Allow:
                default:
                    break;
            }
        }

        var modMeta = new ModerationMeta {
            Action = mod?.Action.ToString().ToLowerInvariant() ?? "unknown",
            Score = mod?.OverallScore ?? 0.0,
            Explanation = mod?.Explanation ?? ""
        };

        // 3) Broadcast via Photon (include moderation metadata)
        var payload = ChatMessagePayload.Create(
            senderId: PhotonNetwork.LocalPlayer?.UserId,
            senderName: PhotonNetwork.LocalPlayer?.NickName,
            roomId: PhotonNetwork.CurrentRoom?.Name,
            text: transcript,
            mod: mod,
            audioUrl: null
        );

        return payload;

        // 4) Optional: queue server-side full moderation (server should do this for robust flow)
        // We assume server handles full-moderation asynchronously for deeper checks and human review.
    }

    private async Task<ModerationResult> CallModerationServer(string text)
    {
        try
        {
            var req = new { text = text, roomId = Photon.Pun.PhotonNetwork.CurrentRoom?.Name, senderId = Photon.Pun.PhotonNetwork.LocalPlayer.UserId };
            var json = JsonSerializer.Serialize(req);
            var resp = await _http.PostAsync(ModerationServerUrl, new StringContent(json, Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Debug.LogWarning("[ChatModerationClient] moderation server returned error: " + resp.StatusCode);
                return null;
            }
            var mod = JsonSerializer.Deserialize<ModerationResult>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return mod;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ChatModerationClient] moderation call failed: " + ex);
            return null;
        }
    }

    private ModerationResult MockModeration(string text)
    {
        // Extremely simple mock for offline dev: block if contains "kill", warn if contains insult words
        var lower = text.ToLowerInvariant();
        if (lower.Contains("kill") || lower.Contains("die")) return new ModerationResult { Action = ModerationAction.Block, OverallScore = 0.95, Explanation = "Violent/self-harm language", Evidence = new[] { "kill" } };
        if (lower.Contains("stupid") || lower.Contains("idiot")) return new ModerationResult { Action = ModerationAction.Warn, OverallScore = 0.65, Explanation = "Insulting language", Evidence = new[] { "stupid" } };
        return new ModerationResult { Action = ModerationAction.Allow, OverallScore = 0.0, Explanation = "", Evidence = Array.Empty<string>() };
    }

    // Redaction example: naive masking of evidence substrings
    private string RedactSensitiveParts(string text, string[] evidence)
    {
        if (evidence == null || evidence.Length == 0) return text;
        string redacted = text;
        foreach (var ev in evidence)
        {
            if (string.IsNullOrWhiteSpace(ev)) continue;
            redacted = redacted.Replace(ev, new string('*', Math.Min(6, ev.Length)));
        }
        return redacted;
    }

    // Example user UI hooks - implement your own UI
    private void ShowUserMessage(string msg) => Debug.Log("[UserMessage] " + msg);

    private Task<bool> ShowWarningAndConfirm(string message)
    {
        // TODO: replace with real modal UI. For now auto-confirm after logging.
        Debug.Log("[Warning] " + message);
        return Task.FromResult(true);
    }
}*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Photon.Pun;

public partial class ChatModerationClient : MonoBehaviour
{
    // simple in-memory cache to avoid re-checking identical messages rapidly
    private readonly ConcurrentDictionary<string, (ModerationResult result, DateTime time)> _modCache = new ConcurrentDictionary<string, (ModerationResult, DateTime)>();
    private readonly TimeSpan _cacheTTL = TimeSpan.FromSeconds(30);

    // Public pluggable transport (set in inspector or via code)
    public IModerationTransport ModerationTransport;

    /// <summary>
    /// Run local quick filters + server fast moderation and return a ChatMessagePayload ready to send.
    /// Returns null when the message is blocked or user cancels send.
    /// </summary>
    public async Task<ChatMessagePayload> HandleVoiceTranscriptAsync(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return null;

        // 1) Local quick filter (fast)
        var quick = LocalQuickFilter.Check(transcript);
        if (quick == LocalQuickFilter.Result.Block)
        {
            ShowUserMessage("Your message contains disallowed content and was blocked.");
            return null;
        }
        else if (quick == LocalQuickFilter.Result.Warn)
        {
            bool cont = await ShowWarningAndConfirm("Your message may violate rules. Send anyway?");
            if (!cont) return null;
        }

        // 2) Cached server check
        ModerationResult mod = null;
        if (_modCache.TryGetValue(transcript, out var entry) && (DateTime.UtcNow - entry.time) < _cacheTTL)
        {
            mod = entry.result;
        }
        else
        {
            try
            {
                if (ModerationTransport != null)
                {
                    mod = await ModerationTransport.FastModerateAsync(transcript, PhotonNetwork.CurrentRoom?.Name, PhotonNetwork.LocalPlayer?.UserId);
                }
                else
                {
                    // fallback: call internal server gateway (if you have)
                    mod = await CallModerationServer(transcript);
                }

                if (mod != null)
                {
                    _modCache[transcript] = (mod, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChatModerationClient] moderation transport failed: {ex}");
            }
        }

        // 3) Handle moderation action
        switch (mod?.Action ?? ModerationAction.Allow)
        {
            case ModerationAction.Block:
                ShowUserMessage("Your message was blocked by moderation.");
                return null;
            case ModerationAction.Warn:
                {
                    bool cont = await ShowWarningAndConfirm($"Possible violation: {mod.Explanation}. Send anyway?");
                    if (!cont) return null;
                }
                break;
            case ModerationAction.Redact:
                transcript = RedactionHelper.Redact(transcript, mod.Evidence ?? Array.Empty<string>());
                break;
            case ModerationAction.Allow:
            default:
                break;
        }

        // 4) Build payload (caller may add AudioUrl)
        var payload = ChatMessagePayload.Create(
            senderId: PhotonNetwork.LocalPlayer?.UserId ?? "local",
            senderName: PhotonNetwork.LocalPlayer?.NickName ?? "Player",
            roomId: PhotonNetwork.CurrentRoom?.Name ?? "room",
            text: transcript,
            mod: new ModerationMeta
            {
                Action = mod?.Action.ToString() ?? "unknown",
                Explanation = mod?.Explanation ?? "",
                Score = mod?.OverallScore ?? 0.0
            },
            audioUrl: null // caller attaches if available
        );

        return payload;
    }

    // Example: small helper stubs you likely already have in file (keep existing implementations)
    private void ShowUserMessage(string msg) => Debug.Log("[ChatModeration] " + msg);

    private Task<bool> ShowWarningAndConfirm(string message)
    {
        // Replace with your actual UI prompt that returns user's choice.
        // For now assume user accepts after a quick dialog.
        Debug.Log("[ChatModeration] Warning: " + message);
        return Task.FromResult(true);
    }

    private Task<ModerationResult> CallModerationServer(string text)
    {
        // Fallback transport - use your gateway client or throw
        // return Task.FromResult<ModerationResult>(null);
        throw new InvalidOperationException("No moderation transport configured.");
    }
}

// -------------------- Local quick filter --------------------
public static class LocalQuickFilter
{
    public enum Result { Allow, Warn, Block }

    private static readonly string[] BlockWords = new[] { "kill yourself", "i will kill" }; // example
    private static readonly string[] WarnWords  = new[] { "idiot", "stupid", "trash" };

    public static Result Check(string text)
    {
        if (string.IsNullOrEmpty(text)) return Result.Allow;
        var lower = text.ToLowerInvariant();
        foreach (var w in BlockWords) if (lower.Contains(w)) return Result.Block;
        foreach (var w in WarnWords)  if (lower.Contains(w)) return Result.Warn;

        // spam heuristic: repeated characters or many links
        if (text.Length > 500) return Result.Warn;
        return Result.Allow;
    }
}
