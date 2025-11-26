using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using Photon.Pun;
/*using PlayFab.Party;
using Photon.Chat;
using ExitGames.Client.Photon;*/

public class ChatFrontEnd : MonoBehaviour
{
    public static ChatFrontEnd instance;

    [Header("Chat UI Elements")]
    public GameObject chatPanel;
    public Button chatToggleButton;
    public TMP_InputField chatInputField;
    public ScrollRect chatScrollRect;
    public Transform chatContentTransform;
    public GameObject chatMessagePrefab;

    [Header("Settings")]
    public int maxMessages = 50;

    private Queue<GameObject> messageQueue = new Queue<GameObject>();

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        if (chatToggleButton != null)
            chatToggleButton.onClick.AddListener(ToggleChatPanel);
        else Debug.LogWarning("Chat toggle button is not assigned.");

        if (chatInputField != null)
            chatInputField.onSubmit.AddListener(OnChatInputSubmit);
        else Debug.LogWarning("Chat input field is not assigned.");

        if (chatPanel != null)
            chatPanel.SetActive(false);
        else Debug.LogWarning("Chat panel is not assigned.");
    }

    private void ToggleChatPanel()
    {
        bool isActive = chatPanel.activeSelf;
        chatPanel.SetActive(!isActive);

        if (!isActive)
        {
            EventSystem.current.SetSelectedGameObject(chatInputField.gameObject, null);
            chatInputField.OnPointerClick(new PointerEventData(EventSystem.current));
        }
    }

    private void OnChatInputSubmit(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            AddMessageToChat("Player: " + message);
            chatInputField.text = string.Empty;
            EventSystem.current.SetSelectedGameObject(chatInputField.gameObject, null);
            chatInputField.OnPointerClick(new PointerEventData(EventSystem.current));
        }
    }

    public void AddMessageToChat(string message)
    {
        GameObject newMessage = Instantiate(chatMessagePrefab, chatContentTransform);
        TMP_Text messageText = newMessage.GetComponent<TMP_Text>();
        if (messageText != null)
        {
            messageText.text = message;
        }

        messageQueue.Enqueue(newMessage);

        if (messageQueue.Count > maxMessages)
        {
            GameObject oldMessage = messageQueue.Dequeue();
            Destroy(oldMessage);
        }

        Canvas.ForceUpdateCanvases();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    public GameObject AddMessageToChatUI(
        string messageId,
        string senderId,
        string senderName,
        string text,
        string timestamp,
        ModerationMeta mod)
    {
        // Instantiate the prefab under the configured chat content transform
        var go = Instantiate(chatMessagePrefab, chatContentTransform);

        // Expect the prefab to have a ChatMessageUI script
        var ui = go.GetComponent<ChatMessageUI>();
        if (ui != null)
        {
            // Use senderName when available, otherwise fall back to senderId
            ui.Setup(messageId, !string.IsNullOrEmpty(senderName) ? senderName : senderId, text, timestamp, mod);
        }
        else
        {
            // If the prefab doesn't have ChatMessageUI, try to put the text on a TMP_Text component
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = $"{(string.IsNullOrEmpty(senderName) ? senderId : senderName)}: {text}";
            }
            else
            {
                Debug.LogWarning("chatMessagePrefab missing ChatMessageUI and TMP_Text; message not displayed correctly.");
            }
        }

        // Maintain the message queue used elsewhere (AddMessageToChat used it)
        messageQueue.Enqueue(go);
        if (messageQueue.Count > maxMessages)
        {
            var old = messageQueue.Dequeue();
            Destroy(old);
        }

        // Force layout update and scroll to bottom
        Canvas.ForceUpdateCanvases();
        if (chatScrollRect != null)
            chatScrollRect.verticalNormalizedPosition = 0f;

        return go;
    }

    // small fix to MarkLocalMessageAsPending to use the same field names
    public void MarkLocalMessageAsPending(string messageId, string text)
    {
        var senderId = PhotonNetwork.LocalPlayer?.UserId;
        var senderName = PhotonNetwork.LocalPlayer?.NickName;
        var timestamp = DateTime.UtcNow.ToString("o");

        // pass null for moderation metadata; adapt if you have a pending/mod placeholder
        var go = AddMessageToChatUI(messageId, senderId, senderName, text, timestamp, null);

        // visually mark 'go' as pending (assumes ChatMessageUI exposes a SetPending method)
        var ui = go.GetComponent<ChatMessageUI>();
        if (ui != null)
        {
            ui.SetPending(true);
        }
    }

}
