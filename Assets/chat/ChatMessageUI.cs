using UnityEngine;
using TMPro;

/// <summary>
/// Handles the setup and display of a single chat message bubble.
/// </summary>
public class ChatMessageUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text senderText;
    public TMP_Text messageText;
    public TMP_Text timestampText;
    public GameObject pendingIndicator; // optional, for pending messages

    private string messageId;
    private bool isPending;

    public void Setup(string messageId, string senderName, string text, string timestamp, ModerationMeta mod)
    {
        this.messageId = messageId;
        if (senderText != null)
            senderText.text = senderName;

        if (messageText != null)
            messageText.text = text;

        if (timestampText != null)
            timestampText.text = timestamp;
        
    }

    public void SetPending(bool pending)
    {
        isPending = pending;
        if (pendingIndicator != null)
            pendingIndicator.SetActive(pending);
    }

    public bool IsPending => isPending;
}