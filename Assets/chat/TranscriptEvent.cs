// TranscriptEvent.cs
using System;

[Serializable]
public class TranscriptEvent
{
    // The transcribed text (final)
    public string Text;

    // Optional: the sender's unique id (PlayFabId, PhotonId, Party user id, etc.)
    public string SenderId;

    // Optional: friendly display name to speak: "Alice", "Bob", etc.
    public string SenderDisplayName;

    // True if this transcript came from the local microphone (useful to avoid re-broadcasting)
    public bool IsLocal;

    public TranscriptEvent() { }

    public TranscriptEvent(string text, string senderId = null, string senderDisplayName = null, bool isLocal = false)
    {
        Text = text;
        SenderId = senderId;
        SenderDisplayName = senderDisplayName;
        IsLocal = isLocal;
    }
}
