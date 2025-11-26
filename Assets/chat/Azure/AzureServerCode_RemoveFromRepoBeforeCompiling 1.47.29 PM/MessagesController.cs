// MessagesController.cs
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/messages")]
public class MessagesController : ControllerBase
{
    private readonly IMessageRepository _repo;
    private readonly IModerationService _moderation;
    private readonly IRealtimePublisher _realtime;
    private readonly IQueueClient _queue;

    public MessagesController(
        IMessageRepository repo,
        IModerationService moderation,
        IRealtimePublisher realtime,
        IQueueClient queue)
    {
        _repo = repo;
        _moderation = moderation;
        _realtime = realtime;
        _queue = queue;
    }

    // Client POSTs here. If audioBytes is provided it'll be saved server-side by the gateway or a pre-signed upload.
    [HttpPost("send")]
    public async Task<IActionResult> ReceiveMessage([FromBody] IncomingMessageDto dto)
    {
        // 1) authenticate user (example - check Authorization header or token)
        if (!User.Identity.IsAuthenticated) return Unauthorized();

        // 2) create message record (pending)
        var messageId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var record = new MessageRecord
        {
            MessageId = messageId,
            RoomId = dto.RoomId,
            UserId = dto.UserId,
            Text = dto.Text,
            AudioUrl = dto.AudioUrl, // optional - stored blob URL
            Status = "pending",
            CreatedAt = now
        };

        await _repo.InsertMessageAsync(record);

        // 3) Fast moderation (low-latency)
        var mod = await _moderation.FastModerateAsync(dto.Text, dto.RoomId, dto.UserId);
        if (mod == null)
        {
            // conservative fallback -> warn
            mod = new ModerationResultDto { Action = "warn", OverallScore = 0.5, Explanation = "moderation-unavailable" };
        }

        // 4) map decision
        if (string.Equals(mod.Action, "block", StringComparison.OrdinalIgnoreCase))
        {
            record.Status = "blocked";
            record.ModerationJson = JsonSerializer.Serialize(mod);
            await _repo.UpdateMessageAsync(record);
            // Optionally notify sender with 403
            return Forbid("Message blocked for policy violation.");
        }

        if (string.Equals(mod.Action, "redact", StringComparison.OrdinalIgnoreCase) && mod.Evidence?.Any() == true)
        {
            record.Text = RedactText(record.Text, mod.Evidence);
        }

        // 5) publish to room (authoritative)
        var payload = new
        {
            messageId = record.MessageId,
            userId = record.UserId,
            text = record.Text,
            sentAt = now,
            moderation = new { action = mod.Action, score = mod.OverallScore }
        };

        await _realtime.BroadcastToRoomAsync(record.RoomId, "CHAT_MESSAGE", payload);

        record.Status = "published";
        record.PublishedAt = DateTime.UtcNow;
        record.ModerationJson = JsonSerializer.Serialize(mod);
        await _repo.UpdateMessageAsync(record);

        // 6) enqueue for deeper async check (full moderation)
        await _queue.EnqueueAsync(new QueueMessage { MessageId = record.MessageId });

        return Ok(new { messageId = record.MessageId });
    }

    // Endpoint used by background worker to remove message
    [HttpPost("remove")]
    public async Task<IActionResult> RemoveMessage([FromBody] RemoveMessageDto remove)
    {
        // authenticate and authorize this call (only worker or admin)
        // update DB
        var rec = await _repo.GetMessageAsync(remove.MessageId);
        if (rec == null) return NotFound();

        rec.Status = "removed";
        rec.RemovedAt = DateTime.UtcNow;
        await _repo.UpdateMessageAsync(rec);

        // broadcast remove event
        var payload = new { messageId = rec.MessageId, reason = remove.Reason ?? "policy violation" };
        await _realtime.BroadcastToRoomAsync(rec.RoomId, "REMOVE_MESSAGE", payload);

        return Ok();
    }

    private static string RedactText(string text, string[] evidence)
    {
        if (string.IsNullOrEmpty(text) || evidence == null) return text;
        string outp = text;
        foreach (var ev in evidence)
            outp = outp.Replace(ev, new string('*', Math.Min(ev.Length, 8)), StringComparison.OrdinalIgnoreCase);
        return outp;
    }
}