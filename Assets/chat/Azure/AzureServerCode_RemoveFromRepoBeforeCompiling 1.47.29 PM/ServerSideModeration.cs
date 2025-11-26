using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache(); // optional
builder.Services.AddHttpClient("azure");
builder.Services.AddRouting();

var app = builder.Build();

var client = new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential());
KeyVaultSecret secret = await client.GetSecretAsync("AzureOpenAIKey");
string openAiKey = secret.Value;

// config from env or secrets
string azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"); // e.g. https://<resource>.openai.azure.com
string azureApiKey  = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
string deploymentId = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODERATION_DEPLOYMENT"); // your moderation deployment name
string apiVersion   = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-11-01-preview"; // update if needed

if (string.IsNullOrEmpty(azureEndpoint) || string.IsNullOrEmpty(azureApiKey) || string.IsNullOrEmpty(deploymentId))
    Console.WriteLine("Warning: Azure OpenAI config not found in env variables.");

var cache = app.Services.GetRequiredService<IMemoryCache>();
var httpFactory = app.Services.GetRequiredService<IHttpClientFactory>();

// simple DTOs

// POST /api/moderate/fast  { text, roomId, senderId }
// returns ModerationResultDto
app.MapPost("/api/moderate/fast", async (ModerationRequestDto req) =>
{
    if (string.IsNullOrWhiteSpace(req?.text)) return Results.BadRequest("text required");

    // Optional caching key
    string cacheKey = $"mod:{req.text.GetHashCode()}";
    if (cache.TryGetValue(cacheKey, out ModerationResultDto cached)) return Results.Ok(cached);

    try
    {
        var client = httpFactory.CreateClient("azure");
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("api-key", azureApiKey);

        // Prepare provider payload (example, adapt if you use a custom classifier deployment)
        var payload = new
        {
            input = req.text
            // add metadata or context if your deployment supports it
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        var url = $"{azureEndpoint}/openai/deployments/{deploymentId}/moderations?api-version={apiVersion}";

        using var resp = await client.PostAsync(url, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            // fallback conservative action
            ModerationResultDto fallback = new ModerationResultDto("warn", 0.5, new(), "moderation service unavailable", Array.Empty<string>());
            // cache short while
            cache.Set(cacheKey, fallback, TimeSpan.FromSeconds(10));
            return Results.Ok(fallback);
        }

        // === Parse provider response ===
        // NOTE: provider response schema may vary depending on your deployment.
        // Here we handle common shapes: try to parse a "categories" or "results" node; adapt as needed.
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // Extract category scores - example for Azure's 'moderations' style
        // Defensive fallbacks if shape differs
        var categories = new Dictionary<string, (string label, double score)>(StringComparer.OrdinalIgnoreCase);
        double overallScore = 0.0;
        string explanation = "";
        var evidence = new List<string>();

        // Example: if response contains "results" array with categories
        if (root.TryGetProperty("results", out var resultsArr) && resultsArr.GetArrayLength() > 0)
        {
            var first = resultsArr[0];
            // provider-specific: look for "categories" boolean map or "category_scores"
            if (first.TryGetProperty("categories", out var cats))
            {
                foreach (var kv in cats.EnumerateObject())
                {
                    bool flagged = kv.Value.GetBoolean();
                    double score = flagged ? 0.9 : 0.0;
                    string label = flagged ? "medium" : "none";
                    categories[kv.Name] = (label, score);
                }
            }
            if (first.TryGetProperty("category_scores", out var catScores))
            {
                foreach (var kv in catScores.EnumerateObject())
                {
                    double score = kv.Value.GetDouble();
                    string label = score > 0.75 ? "severe" : score > 0.5 ? "medium" : score > 0.2 ? "low" : "none";
                    categories[kv.Name] = (label, score);
                    overallScore = Math.Max(overallScore, score);
                }
            }
            // optional textual explanations
            if (first.TryGetProperty("explanation", out var expl)) explanation = expl.GetString() ?? "";
        }
        else if (root.TryGetProperty("category_scores", out var categoryScoresRoot))
        {
            foreach (var kv in categoryScoresRoot.EnumerateObject())
            {
                double score = kv.Value.GetDouble();
                string label = score > 0.75 ? "severe" : score > 0.5 ? "medium" : score > 0.2 ? "low" : "none";
                categories[kv.Name] = (label, score);
                overallScore = Math.Max(overallScore, score);
            }
        }
        else
        {
            // fallback try to find "label" or "safety" fields
            // (left intentionally flexible)
        }

        // Evidence: try to extract any "matched" items if present
        if (root.TryGetProperty("evidence", out var ev))
        {
            foreach (var item in ev.EnumerateArray())
            {
                evidence.Add(item.GetString() ?? "");
            }
        }

        // decision mapping to action:
        string action = MapScoresToAction(overallScore, categories);

        ModerationResultDto result = new ModerationResultDto(action, overallScore, categories, explanation, evidence.ToArray());

        // cache (short TTL)
        cache.Set(cacheKey, result, TimeSpan.FromSeconds(20));
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        var err = new ModerationResultDto("warn", 0.5, new(), $"moderation error: {ex.Message}", Array.Empty<string>());
        return Results.Ok(err);
    }
});

app.Run();



// helper mapping - tune thresholds per category
string MapScoresToAction(double overall, Dictionary<string,(string label,double score)> cats)
{
    // If any category severe -> block
    foreach (var kv in cats)
    {
        if (kv.Value.label == "severe" || kv.Value.score >= 0.9) return "block";
    }
    if (overall >= 0.9) return "block";
    if (overall >= 0.6) return "warn";
    if (overall >= 0.4) return "redact";
    return "allow";
}

// pseudocode in gateway controller or message handler
public class GatewayController
{
    // Assume dependencies are injected or available in this class
    private readonly IDatabase _db;
    private readonly IModerationService _moderationService;
    private readonly IRealtimeService _realtime;
    private readonly IQueueService _queue;

    public GatewayController(IDatabase db, IModerationService moderationService, IRealtimeService realtime, IQueueService queue)
    {
        _db = db;
        _moderationService = moderationService;
        _realtime = realtime;
        _queue = queue;
    }

    public async Task<IActionResult> ReceiveMessage([FromBody] IncomingMessageDto msg)
    {
        if (string.IsNullOrWhiteSpace(msg.Text)) return BadRequest();

        // 1) authenticate sender (validate JWT/PlayFab/Photon token)
        if (!AuthenticateSender(msg.UserId)) return Unauthorized();

        // 2) create message record
        var messageId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var record = new MessageRecord {
            MessageId = messageId,
            RoomId = msg.RoomId,
            UserId = msg.UserId,
            Text = msg.Text,
            AudioUrl = msg.AudioUrl,
            Status = "pending",
            CreatedAt = now
        };
        await _db.InsertMessageAsync(record);

        // 3) fast moderation call (ServerSideModeration)
        var mod = await _moderationService.FastModerateAsync(msg.Text, msg.RoomId, msg.UserId);
        if (mod == null) {
            // conservative fallback - WARN
            mod = new ModerationResultDto("warn", 0.5, new(), "moderation-unavailable", Array.Empty<string>());
        }

        // 4) map action -> decision
        if (mod.Action.Equals("block", StringComparison.OrdinalIgnoreCase)) {
            record.Status = "blocked";
            record.Moderation = Serialize(mod);
            await _db.UpdateMessageAsync(record);
            // notify sender
            return Forbid("Message blocked for policy violation.");
        }

        // Allow / Warn / Redact:
        if (mod.Action.Equals("redact", StringComparison.OrdinalIgnoreCase) && mod.Evidence?.Length>0) {
            msg = msg with { Text = Redact(msg.Text, mod.Evidence) };
            record.Text = msg.Text;
        }

        // 5) publish to room (include messageId and moderation meta if you want)
        var payload = new {
            messageId,
            userId = msg.UserId,
            text = msg.Text,
            sentAt = now,
            moderation = new { action = mod.Action, score = mod.OverallScore }
        };
        _realtime.BroadcastToRoom(msg.RoomId, "CHAT_MESSAGE", payload);

        record.Status = "published";
        record.PublishedAt = DateTime.UtcNow;
        record.Moderation = Serialize(mod);
        await _db.UpdateMessageAsync(record);

        // 6) enqueue for async deep moderation
        await _queue.EnqueueAsync(new { messageId, roomId = msg.RoomId, userId = msg.UserId });

        return Ok(new { messageId });
    }

    // Dummy implementations for missing methods
    private bool AuthenticateSender(string userId) => true;
    private IActionResult BadRequest() => new BadRequestResult();
    private IActionResult Unauthorized() => new UnauthorizedResult();
    private IActionResult Forbid(string message) => new ForbidResult();
    private IActionResult Ok(object value) => new OkObjectResult(value);
    private string Serialize(object obj) => JsonSerializer.Serialize(obj);
    private string Redact(string text, string[] evidence) => text; // Implement redaction logic
}

// Dummy interfaces and classes for compilation
public interface IDatabase
{
    Task InsertMessageAsync(MessageRecord record);
    Task UpdateMessageAsync(MessageRecord record);
}
public interface IModerationService
{
    Task<ModerationResultDto> FastModerateAsync(string text, string roomId, string userId);
}
public interface IRealtimeService
{
    void BroadcastToRoom(string roomId, string eventType, object payload);
}
public interface IQueueService
{
    Task EnqueueAsync(object item);
}
public interface IMessageRepository
{
    Task InsertMessageAsync(MessageRecord rec);
    Task UpdateMessageAsync(MessageRecord rec);
    Task<MessageRecord> GetMessageAsync(string messageId);
    Task<IEnumerable<MessageRecord>> GetRecentMessages(string roomId, int limit = 20);
}
public interface IQueueClient
{
    Task EnqueueAsync(object item);
}

public class AudioController
{
    private readonly BlobServiceClient _blobServiceClient;

    public AudioController(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    [HttpPost("upload-audio")]
    public async Task<IActionResult> UploadAudio(HttpRequest request)
    {
        // authenticate
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        var bytes = ms.ToArray();

        // create filename
        var blobName = $"asr_{Guid.NewGuid():N}.wav";
        var blobClient = _blobServiceClient.GetBlobContainerClient("asr-temp").GetBlobClient(blobName);
        await blobClient.UploadAsync(new BinaryData(bytes), overwrite: false);
        // set metadata to exclude from backup - not applicable in blob, but set tags/expiry
        // optionally create SAS token
        var sasUri = blobClient.GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(2));
        return new OkObjectResult(new { blobUrl = blobClient.Uri.ToString(), sasUrl = sasUri.ToString() });
    }
}

// The following helper class wraps SendToGatewayAsync and UploadResponse.
public static class GatewayHelper
{
    public static async Task<bool> SendToGatewayAsync(string roomId, string userId, string text, byte[] audioWav = null)
    {
        var url = "https://gateway.example.com/api/messages/send";
        var dto = new { RoomId = roomId, UserId = userId, Text = text, AudioUrl = (string)null };

        if (audioWav != null)
        {
            // upload audio first
            var uploadUrl = "https://gateway.example.com/api/messages/upload-audio";
            using var uw = new UnityWebRequest(uploadUrl, UnityWebRequest.kHttpVerbPOST);
            uw.uploadHandler = new UploadHandlerRaw(audioWav);
            uw.downloadHandler = new DownloadHandlerBuffer();
            uw.SetRequestHeader("Content-Type", "application/octet-stream");
            await uw.SendWebRequest();
            if (uw.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Audio upload failed: " + uw.error);
            }
            else
            {
                var j = uw.downloadHandler.text;
                var parsed = JsonUtility.FromJson<UploadResponse>(j);
                dto = new { RoomId = roomId, UserId = userId, Text = text, AudioUrl = parsed.blobUrl };
            }
        }

        var json = JsonUtility.ToJson(dto);
        using (var uw = UnityWebRequest.Put(url, json))
        {
            uw.method = UnityWebRequest.kHttpVerbPOST;
            uw.SetRequestHeader("Content-Type", "application/json");
            // attach auth header if required
            await uw.SendWebRequest();
            if (uw.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("Send failed: " + uw.error);
                return false;
            }
            return true;
        }
    }

    [Serializable]
    public class UploadResponse
    {
        public string blobUrl;
    }
}


public class BadRequestResult : IActionResult { }
public class UnauthorizedResult : IActionResult { }
public class ForbidResult : IActionResult { }
public class OkObjectResult : IActionResult
{
    public OkObjectResult(object value) { }
}
public interface IActionResult { }
public class ModerationQueueProcessor
{
    private readonly IDatabase _db;
    private readonly IModerationService _moderationService;
    private readonly IRealtimeService _realtime;
    private readonly IModeratorQueue _moderatorQueue;

    public ModerationQueueProcessor(IDatabase db, IModerationService moderationService, IRealtimeService realtime, IModeratorQueue moderatorQueue)
    {
        _db = db;
        _moderationService = moderationService;
        _realtime = realtime;
        _moderatorQueue = moderatorQueue;
    }

    public async Task ProcessQueueItemAsync(string messageId)
    {
        var record = await _db.GetMessageAsync(messageId);
        if (record == null || record.Status == "removed") return;

        // gather context: last N messages, user history, audio blob URL (secure SAS token)
        var context = await _db.GetRecentMessages(record.RoomId, limit: 20);
        var fullMod = await _moderationService.FullModerateAsync(record.Text, context, record.UserId, record.AudioUrl);

        if (fullMod == null) return; // log and retry per policy

        // if fullModeration decides to block or escalate
        if (fullMod.Action.Equals("block", StringComparison.OrdinalIgnoreCase) || fullMod.Action.Equals("escalate", StringComparison.OrdinalIgnoreCase))
        {
            // 1) Update DB
            record.Status = "removed";
            record.Moderation = Serialize(fullMod);
            record.RemovedAt = DateTime.UtcNow;
            await _db.UpdateMessageAsync(record);

            // 2) Instruct gateway to remove message (send Remove event to room)
            var payload = new { messageId = record.MessageId, reason = fullMod.Explanation };
            _realtime.BroadcastToRoom(record.RoomId, "REMOVE_MESSAGE", payload);

            // 3) Enqueue for human review / alert moderators
            await _moderatorQueue.EnqueueAsync(new { messageId = record.MessageId, userId = record.UserId, details = fullMod });
        }
        else
        {
            // update DB with full moderation findings for audit
            record.Moderation = Serialize(fullMod);
            await _db.UpdateMessageAsync(record);
        }
    }
}
public class InMemoryMessageRepository : IMessageRepository
{
    private readonly ConcurrentDictionary<string, MessageRecord> _store = new();

    public Task InsertMessageAsync(MessageRecord rec) { _store[rec.MessageId] = rec; return Task.CompletedTask; }
    public Task UpdateMessageAsync(MessageRecord rec) { _store[rec.MessageId] = rec; return Task.CompletedTask; }
    public Task<MessageRecord> GetMessageAsync(string messageId) => Task.FromResult(_store.TryGetValue(messageId, out var r) ? r : null);
    public Task<IEnumerable<MessageRecord>> GetRecentMessages(string roomId, int limit = 20) =>
        Task.FromResult(_store.Values.Where(m => m.RoomId == roomId && m.Status == "published").OrderByDescending(m => m.PublishedAt).Take(limit).AsEnumerable());
}
public class ServiceBusQueueClient : IQueueClient, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    public ServiceBusQueueClient(string connectionString, string queueName)
    {
        _client = new ServiceBusClient(connectionString);
        _sender = _client.CreateSender(queueName);
    }
    public async Task EnqueueAsync(object item)
    {
        var json = JsonSerializer.Serialize(item);
        await _sender.SendMessageAsync(new ServiceBusMessage(json));
    }
    public async ValueTask DisposeAsync() { await _sender.DisposeAsync(); await _client.DisposeAsync(); }
}
public class QueueMessage { public string MessageId { get; set; } }


public record ModerationRequestDto(string text, string roomId = null, string senderId = null);
public record ModerationResultDto(string action, double overallScore, Dictionary<string, (string label, double score)> categories, string explanation, string[] evidence);