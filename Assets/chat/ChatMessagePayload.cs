// ChatMessagePayload.cs (Shared)
using System;

[Serializable]
public class ModerationMeta
{
    public string Action;    // allow|warn|redact|block|escalate
    public double Score;
    public string Explanation;
}

[Serializable]
public class ChatMessagePayload
{
    public string MessageId;
    public string SenderId;
    public string SenderName;
    public string RoomId;
    public string Text;
    public string AudioUrl;
    public string Timestamp; // ISO 8601
    public ModerationMeta ModerationMeta;

    public static ChatMessagePayload Create(string senderId, string senderName, string roomId, string text,
                                            ModerationMeta mod = null, string audioUrl = null)
    {
        return new ChatMessagePayload {
            MessageId = Guid.NewGuid().ToString("N"),
            SenderId = senderId,
            SenderName = senderName,
            RoomId = roomId,
            Text = text,
            AudioUrl = audioUrl,
            Timestamp = DateTime.UtcNow.ToString("o"),
            ModerationMeta = mod
        };
    }
}
