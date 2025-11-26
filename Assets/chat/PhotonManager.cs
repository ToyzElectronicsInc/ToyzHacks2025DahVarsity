using ExitGames.Client.Photon;
using Newtonsoft.Json;
using PlayFab;
using PlayFab.ClientModels;
using System;
using System.Text;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Photon.Realtime;
using Photon.Pun;
using Photon.Pun.Demo.PunBasics;
using UnityEngine;
// #if UNITY_IOS
// using UnityEngine.SocialPlatforms.GameCenter;
// #endif
// #if UNITY_ANDROID
// using GooglePlayGames;
// using GooglePlayGames.BasicApi;
// #endif

public class PhotonManager : MonoBehaviourPunCallbacks
{
    public static PhotonManager Instance { get; private set; }
    public IChatAI ChatAI { get; private set; }
    public event Action OnPhotonConnected;
    public event Action OnRoomJoined;
    public enum LogLevel { Info, Warning, Error }


    
    public string CurrentMap => PhotonNetwork.CurrentRoom?.CustomProperties["map"] as string;
    public string CurrentMode => PhotonNetwork.CurrentRoom?.CustomProperties["mode"] as string;

    private readonly int _maxMainThreadActionsPerFrame = 50; // tune
    private readonly int _maxSendBatchPerFrame = 5; // tune
    private readonly int _maxMainThreadActionsQueueCapacity = 1000; // prevent unbounded growth

    private readonly ConcurrentQueue<ChatMessagePayload> _sendQueue = new ConcurrentQueue<ChatMessagePayload>();
    private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
    private readonly SemaphoreSlim _ttsSemaphore = new SemaphoreSlim(1, 1);

    public enum PhotonEventCode : byte
    {
        ChatMessage = 1,
        PlayerReady = 2,
        QuestTrigger = 3
    };

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeChatAI();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void InitializeChatAI()
    {
#if UNITY_STANDALONE_WIN || UNITY_ANDROID || UNITY_WEBGL
        var llm = new NvidiaLLMClient();
        var tts = FindObjectOfType<RivaTtsClient>();
        var asr = FindObjectOfType<RivaStreamingASR>();
        var transcriber = FindObjectOfType<RivaTranscriptionClient>();
        NvidiaChatAI.Initialize(llm, tts, asr, transcriber);
        ChatAI = NvidiaChatAI.Instance;
#elif UNITY_IOS
        ChatAI = AppleIntelligenceService.Instance;
#endif

        if (ChatAI != null)
        {
            // Avoid double-subscribe: ensure only one subscription exists
            ChatAI.OnFinalTranscript -= HandleFinalTranscript_Enqueue;
            ChatAI.OnFinalTranscript += HandleFinalTranscript_Enqueue;
        }
    }

    private void HandleFinalTranscript_Enqueue(TranscriptEvent evt)
    {
        // quick sanity check on background thread; push to main thread queue
        if (evt == null || string.IsNullOrWhiteSpace(evt.Text)) return;
        _mainThreadActions.Enqueue(() => _ = HandleVoiceTranscript_MainThreadAsync(evt));
    }

    private async Task HandleVoiceTranscript_MainThreadAsync(TranscriptEvent evt)
    {
        try
        {
            // 1) Broadcast transcript via Photon (only do this if it's local OR you want to forward remote ASR)
            // Decide policy: broadcast local transcripts (isLocal==true). If remote transcripts should NOT be rebroadcast, skip.
            if (evt.IsLocal)
            {
                SendMessage(evt.Text);
            }
            else
            {
                // If you still want to broadcast remote transcripts to all clients as chat messages:
                // SendMessage($"{evt.SenderDisplayName ?? "Someone"}: {evt.Text}");
                // Or skip broadcasting if the origin already broadcasted it.
            }

            // 2) Compose announcement for TTS: include name if available and not the local user speaking.
            string announcement;
            bool shouldAnnounce = true; // toggle if you only want announcements for remote users

            if (!shouldAnnounce)
            {
                return;
            }

            // If transcript is from local user, you probably don't want to TTS "You say..." locally.
            if (evt.IsLocal)
            {
                // Optionally skip speaking local transcripts.
                return;
            }

            string speaker = string.IsNullOrWhiteSpace(evt.SenderDisplayName) ? "Someone" : evt.SenderDisplayName;
            announcement = $"{speaker} says: {evt.Text}";

            // Throttle concurrent TTS
            await _ttsSemaphore.WaitAsync();
            try
            {
                AudioClip clip = null;
                try
                {
                    clip = await ChatAI.SpeakAsync(announcement);
                }
                catch (Exception speakEx)
                {
                    Debug.LogWarning($"ChatAI.SpeakAsync failed: {speakEx.Message}");
                }

                if (clip != null)
                {
                    PlayClip(clip);
                }
            }
            finally
            {
                _ = _ttsSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"HandleVoiceTranscript_MainThreadAsync error: {ex}");
        }
    }

    private void Start()
    {
        LoginAndConnectPhoton();
    }

    public void LoginAndConnectPhoton()
    {
        PlayFabClientAPI.LoginWithCustomID(new LoginWithCustomIDRequest
        {
            CustomId = SystemInfo.deviceUniqueIdentifier,
            CreateAccount = true
        },
        result =>
        {
            Log("PlayFab login successful", LogLevel.Info);
            PlayFabManager.Instance.PlayFabId = result.PlayFabId;

            PhotonNetwork.NickName = result.PlayFabId;
            PhotonNetwork.ConnectUsingSettings();
        },
        error =>
        {
            Log("PlayFab login failed: " + error.GenerateErrorReport(), LogLevel.Error);
        });
    }

    public override void OnJoinedLobby()
    {
        Log("Joined Photon Lobby", LogLevel.Info);
    }

    [SerializeField] private byte maxPlayers = 10;

    public void CreateOrJoinRoom(string roomName)
    {
        PhotonNetwork.JoinOrCreateRoom(roomName, GetDefaultRoomOptions(), TypedLobby.Default);
    }

    public override void OnConnectedToMaster()
    {
        Log("Connected to Photon Master Server", LogLevel.Info);
        OnPhotonConnected?.Invoke();
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Log("JoinRandomRoom failed, creating a new room.", LogLevel.Warning);
        CreateOrJoinRoom("Room_" + UnityEngine.Random.Range(1000, 9999));
    }

    private void Update()
    {
        int ran = 0;
        while (ran < _maxMainThreadActionsPerFrame && _mainThreadActions.TryDequeue(out var action))
        {
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogError($"Main-thread action failed: {ex}"); }
            ran++;
        }

        // process outbound chat sends (limited per frame)
        int processed = 0;
        while (processed < _maxSendBatchPerFrame && _sendQueue.TryDequeue(out var p))
        {
            try
            {
                EnsurePayloadMainThreadDefaults(p);
                // keep using JSON for now
                var json = JsonConvert.SerializeObject(p);
                var bytes = Encoding.UTF8.GetBytes(json);
                var options = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
                var sendOptions = new SendOptions { Reliability = true };
                PhotonNetwork.RaiseEvent((byte)PhotonEventCode.ChatMessage, bytes, options, sendOptions);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhotonManager] RaiseEvent failed: {ex}");
            }
            processed++;
        }
    }

    private RoomOptions GetDefaultRoomOptions()
    {
        return new RoomOptions
        {
            MaxPlayers = maxPlayers,
            CustomRoomProperties = new ExitGames.Client.Photon.Hashtable {
                { "map", "Forest" },
            { "mode", "Coop" }
            },
            CustomRoomPropertiesForLobby = new[] { "map", "mode" }
        };
    }

    public override void OnJoinedRoom()
    {
        // Defensive: ensure room is available
        var roomName = PhotonNetwork.CurrentRoom?.Name ?? "unknown";
        Log($"Joined Photon Room: {roomName}", LogLevel.Info);

        // Notify subscribers that we've joined the room
        OnRoomJoined?.Invoke();

        // Attempt to initialize PartyManager safely (if available)
        try
        {
            var playFabId = PlayFabManager.Instance?.PlayFabId;
            if (string.IsNullOrEmpty(playFabId))
            {
                Log("PlayFabId is not available yet. PartyManager initialization skipped.", LogLevel.Warning);
            }
            else if (PartyManager.Instance == null)
            {
                Log("PartyManager.Instance is null — cannot initialize network.", LogLevel.Warning);
            }
            else
            {
                PartyManager.Instance.InitializeNetwork(playFabId);
            }
        }
        catch (Exception ex)
        {
            Log($"Exception while initializing PartyManager network: {ex}", LogLevel.Error);
        }

        // Instead of calling GameManager directly, raise an event so interested systems (including GameManager)
        // can subscribe to OnMultiplayerReadyRequested. This decouples PhotonManager from game logic.
        try
        {
            OnMultiplayerReadyRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Log($"OnMultiplayerReadyRequested handlers threw: {ex}", LogLevel.Warning);
        }
    }


    // PhotonManager.cs (inside the PhotonManager class, e.g. after GetDefaultRoomOptions())
    /*private static void EnsurePayloadDefaults(ChatMessagePayload p)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));

        if (string.IsNullOrEmpty(p.MessageId))
            p.MessageId = Guid.NewGuid().ToString("N");

        if (string.IsNullOrEmpty(p.Timestamp))
            p.Timestamp = DateTime.UtcNow.ToString("o");

        if (string.IsNullOrEmpty(p.RoomId))
            p.RoomId = PhotonNetwork.CurrentRoom?.Name ?? "unknown";

        if (string.IsNullOrEmpty(p.SenderId))
            p.SenderId = PhotonNetwork.LocalPlayer?.UserId ?? "local";
    }*/

    // PhotonManager.cs (inside PhotonManager class)
    // Keep this simple and safe for background threads:
    private static void EnsurePayloadThreadSafeDefaults(ChatMessagePayload p)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));
        if (string.IsNullOrEmpty(p.MessageId)) p.MessageId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(p.Timestamp)) p.Timestamp = DateTime.UtcNow.ToString("o");
        if (p.Text == null) p.Text = "";
    }
    // Call only on main thread (Update) before sending over Photon

    private void EnsurePayloadMainThreadDefaults(ChatMessagePayload p)
    {
        if (p == null) return;
        if (string.IsNullOrEmpty(p.RoomId)) p.RoomId = PhotonNetwork.CurrentRoom?.Name ?? "unknown";
        if (string.IsNullOrEmpty(p.SenderId)) p.SenderId = PhotonNetwork.LocalPlayer?.UserId ?? "local";
        if (string.IsNullOrEmpty(p.SenderName)) p.SenderName = PhotonNetwork.LocalPlayer?.NickName ?? p.SenderName;
    }

    public void SendChatMessage(ChatMessagePayload payload)
    {
        /*var ht = new Hashtable
        {
            { "messageId", payload.MessageId },
            { "senderId", payload.SenderId },
            { "senderName", payload.SenderName },
            { "text", payload.Text },
            { "audioUrl", payload.AudioUrl ?? "" },
            { "timestamp", payload.Timestamp.ToString("o") }
        };

        if (payload.ModerationMeta != null)
        {
            var modHt = new Hashtable
            {
                { "action", payload.ModerationMeta.Action ?? "" },
                { "score", payload.ModerationMeta.Score },
                { "explain", payload.ModerationMeta.Explanation ?? "" }
            };
            ht["moderation"] = modHt;
        }

        var options = new RaiseEventOptions { Receivers = ReceiverGroup.All };
        var sendOptions = new ExitGames.Client.Photon.SendOptions { Reliability = true };
        PhotonNetwork.RaiseEvent((byte)PhotonEventCode.ChatMessage, ht, options, sendOptions);*/

        /*if (payload == null) throw new ArgumentNullException(nameof(payload));

        // Ensure basic metadata
        if (string.IsNullOrEmpty(payload.MessageId)) payload.MessageId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrEmpty(payload.Timestamp)) payload.Timestamp = DateTime.UtcNow.ToString("o");
        if (string.IsNullOrEmpty(payload.RoomId))
        {
            payload.RoomId = PhotonNetwork.CurrentRoom?.Name ?? "unknown";
        }
        if (string.IsNullOrEmpty(payload.SenderId))
        {
            payload.SenderId = PhotonNetwork.LocalPlayer?.UserId ?? "local";
        }

        string json;
        try
        {
            json = JsonConvert.SerializeObject(payload);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PhotonManager] Failed to serialize ChatMessagePayload: {ex}");
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);

        var options = new RaiseEventOptions
        {
            Receivers = ReceiverGroup.Others // broadcast to other clients
        };

        var sendOptions = new SendOptions { Reliability = true };

        try
        {
            PhotonNetwork.RaiseEvent(ChatEventCode, bytes, options, sendOptions);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PhotonManager] RaiseEvent failed: {ex}");
        }*/

        if (payload == null) throw new ArgumentNullException(nameof(payload));

        // Normalise/ensure minimal metadata centrally
        EnsurePayloadThreadSafeDefaults(payload);

        // Enqueue for batched sending from Update()
        _sendQueue.Enqueue(payload);
    }

    public void SendMessage(string message)
    {
        var payload = ChatMessagePayload.Create(
            senderId: PhotonNetwork.LocalPlayer?.UserId,
            senderName: PhotonNetwork.LocalPlayer?.NickName,
            roomId: PhotonNetwork.CurrentRoom?.Name,
            text: message,
            mod: null
        );
        SendChatMessage(payload);
    }
    public void BroadcastRemoveMessage(string messageId, string reason = null)
    {
        if (string.IsNullOrEmpty(messageId)) return;
        var ht = new Hashtable { ["messageId"] = messageId, ["reason"] = reason ?? "" };
        var options = new RaiseEventOptions { Receivers = ReceiverGroup.All };
        var sendOptions = new ExitGames.Client.Photon.SendOptions { Reliability = true };
        PhotonNetwork.RaiseEvent((byte)PhotonEventCode.QuestTrigger + 200, ht, options, sendOptions);
        // NOTE: If you prefer to add a dedicated RemoveMessage code, add to PhotonEventCode enum (e.g., RemoveMessage = 201)
    }

    public void OnPlayerEnteredRoom(Player newPlayer)
    {
        Log("Player Joined: " + newPlayer.NickName, LogLevel.Info);
    }

    public void OnPlayerReady(string playerName)
    {
        string message = $"{playerName} is ready to begin!";
        PhotonNetwork.RaiseEvent((byte)PhotonEventCode.PlayerReady, message,
            new RaiseEventOptions { Receivers = ReceiverGroup.All },
            new ExitGames.Client.Photon.SendOptions { Reliability = true });
    }


    public void OnPlayerLeftRoom(Player otherPlayer)
    {
        Log("Player Left: " + otherPlayer.NickName, LogLevel.Info);
    }

    private void PlayClip(AudioClip clip)
    {
        var src = GetComponent<AudioSource>();
        if (src == null) src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.Play();
    }

    public override void OnEnable()
    {
        //base.OnEnable();
        PhotonNetwork.NetworkingClient.EventReceived += OnEvent;
    }

    public override void OnDisable()
    {
        if (ChatAI != null) ChatAI.OnFinalTranscript -= HandleFinalTranscript_Enqueue;
        PhotonNetwork.NetworkingClient.EventReceived -= OnEvent;
    }

    private void OnDestroy()
    {
        if (ChatAI != null) ChatAI.OnFinalTranscript -= HandleFinalTranscript_Enqueue;
    }

    /*private void OnEvent(ExitGames.Client.Photon.EventData photonEvent)
    {
        if (photonEvent.Code == (byte)PhotonEventCode.ChatMessage)
        {
            string message = photonEvent.CustomData as string;
            Player senderPlayer = PhotonNetwork.CurrentRoom.GetPlayer(photonEvent.Sender);
            string senderName = senderPlayer?.NickName ?? "Unknown";
            ChatFrontEnd.instance.AddMessageToChat($"{senderName}: {message}");
        }
        if (rivaTtsClient != null)
        {
            AudioClip clip = await rivaTtsClient.GenerateSpeechClip($"{senderName} says: {message}");
            PlayClip(clip);
        }
    }*/

    /*private async void OnEvent(ExitGames.Client.Photon.EventData photonEvent)
    {
        string message = photonEvent.CustomData as string;
        Player senderPlayer = PhotonNetwork.CurrentRoom.GetPlayer(photonEvent.Sender);
        string senderName = senderPlayer?.NickName ?? "Unknown";

        switch ((PhotonEventCode)photonEvent.Code)
        {
            case PhotonEventCode.ChatMessage:
                if (photonEvent.Code == (byte)PhotonChatCode.ChatMessage)
                {
                    var ht = photonEvent.CustomData as Hashtable;
                    if (ht != null) HandleIncomingChatHashtable(ht);
                }
                else if (photonEvent.Code == (byte)PhotonChatCode.RemoveMessage)
                {
                    var ht = photonEvent.CustomData as Hashtable;
                    var messageId = ht?["messageId"] as string;
                    var reason = ht?["reason"] as string;
                    RemoveMessageFromUI(messageId, reason);
                }
                break;

            case PhotonEventCode.PlayerReady:
                ChatFrontEnd.instance.AddSystemMessage(message); // styled differently
                {
                    var ht = photonEvent.CustomData as Hashtable;
                    var messageId = ht?["messageId"] as string;
                    var reason = ht?["reason"] as string;
                    RemoveMessageFromUI(messageId, reason);
                }
                break;

            case PhotonEventCode.PlayerReady:
                ChatFrontEnd.instance.AddSystemMessage(message); // styled differently
                if (ChatAI != null)
                {
                    try
                    {
                        AudioClip clip = await ChatAI.SpeakAsync(message);
                        if (clip != null) PlayClip(clip);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"ChatAI.SpeakAsync failed: {ex.Message}");
                    }
                }
        }
        break;

        // Add other cases like QuestTrigger here
    }*/

    private void OnEvent(EventData photonEvent)
    {
        try
        {
            // We expect ChatMessage event code to be (byte)PhotonEventCode.ChatMessage
            if (photonEvent.Code == (byte)PhotonEventCode.ChatMessage)
            {
                // Photon may deliver byte[] or Hashtable depending on RaiseEvent usage.
                // Here we support serialized JSON bytes (recommended) and fallback to Hashtable.
                if (photonEvent.CustomData is byte[] bytes)
                {
                    var json = Encoding.UTF8.GetString(bytes);
                    var payload = JsonConvert.DeserializeObject<ChatMessagePayload>(json);
                    if (payload != null) HandleIncomingChatPayload(payload, photonEvent.Sender);
                    return;
                }
                else if (photonEvent.CustomData is string s)
                {
                    var payload = JsonConvert.DeserializeObject<ChatMessagePayload>(s);
                    if (payload != null) HandleIncomingChatPayload(payload, photonEvent.Sender);
                    return;
                }
                else if (photonEvent.CustomData is Hashtable ht)
                {
                    // fallback legacy handling
                    HandleIncomingChatHashtable(ht, photonEvent.Sender);
                    return;
                }
            }
            else if (photonEvent.Code == (byte)PhotonEventCode.PlayerReady)
            {
                // example system message; payload may be a string or HT
                if (photonEvent.CustomData is string msg) ChatFrontEnd.instance?.AddSystemMessage(msg);
            }
            // Add other event codes here...
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PhotonManager] OnEvent processing error: {ex}");
        }
    }

    // new helper: unify incoming chat processing by payload
    private void HandleIncomingChatPayload(ChatMessagePayload payload, int senderPhotonId)
    {
        if (payload == null) return;

        // If senderPhotonId available, attempt to resolve display name if needed:
        Player senderPlayer = PhotonNetwork.CurrentRoom?.GetPlayer(senderPhotonId);
        var senderName = payload.SenderName ?? senderPlayer?.NickName ?? "Unknown";

        // Respect moderation meta
        var action = payload.ModerationMeta?.Action?.ToLowerInvariant();
        if (action == "block")
        {
            ChatFrontEnd.instance?.ShowSystemMessage($"{senderName}'s message was blocked by policy.");
            return;
        }
        if (action == "redact")
        {
            var redacted = RedactionHelper.Redact(payload.Text, null);
            var uiElement = ChatFrontEnd.instance?.AddMessageToChatUI(payload.MessageId, payload.SenderId, senderName, redacted, payload.Timestamp, payload.ModerationMeta);
            if (uiElement != null) uiMessageMap[payload.MessageId] = uiElement;
            return;
        }

        // Default: show message and attach audio if provided
        var uiElem = ChatFrontEnd.instance?.AddMessageToChatUI(payload.MessageId, payload.SenderId, senderName, payload.Text, payload.Timestamp, payload.ModerationMeta);
        if (uiElem != null) uiMessageMap[payload.MessageId] = uiElem;

        // Optionally prefetch audio for playback (async)
        if (!string.IsNullOrEmpty(payload.AudioUrl))
        {
            _ = PreloadAndPlayAudioForMessage(payload.MessageId, payload.AudioUrl);
        }
    }
    
    private readonly Dictionary<string, GameObject> uiMessageMap = new Dictionary<string, GameObject>();

    private void HandleIncomingChatHashtable(Hashtable ht)
    {
        string messageId = ht.ContainsKey("messageId") ? ht["messageId"] as string : Guid.NewGuid().ToString("N");
        string senderId = ht.ContainsKey("senderId") ? ht["senderId"] as string : "";
        string senderName = ht.ContainsKey("senderName") ? ht["senderName"] as string : "";
        string text = ht.ContainsKey("text") ? ht["text"] as string : "";
        string audioUrl = ht.ContainsKey("audioUrl") ? ht["audioUrl"] as string : "";
        string timestamp = ht.ContainsKey("timestamp") ? ht["timestamp"] as string : "";

        ModerationMeta modMeta = null;
        if (ht.ContainsKey("moderation"))
        {
            var m = ht["moderation"] as Hashtable;
            if (m != null)
            {
                modMeta = new ModerationMeta
                {
                    Action = m.ContainsKey("action") ? m["action"] as string : "",
                    Score  = m.ContainsKey("score") ? (double)(m["score"]) : 0.0,
                    Explanation = m.ContainsKey("explain") ? m["explain"] as string : ""
                };
            }
        }

        // Add to UI and record messageId -> UI element mapping
        var uiElement = ChatFrontEnd.instance?.AddMessageToChatUI(messageId, senderId, senderName, text, timestamp, modMeta);
        if (uiElement != null)
        {
            uiMessageMap[messageId] = uiElement;
        }
    }

    private void RemoveMessageFromUI(string messageId, string reason = null)
    {
        if (string.IsNullOrEmpty(messageId)) return;
        if (uiMessageMap.TryGetValue(messageId, out var elem))
        {
            var chatItem = elem.GetComponent<ChatMessageUI>();
            if (chatItem != null) chatItem.MarkRemoved(reason ?? "Removed for policy violation");
            else Destroy(elem);
            uiMessageMap.Remove(messageId);
        }
        else
        {
            Debug.Log($"[PhotonManager] RemoveMessage for unknown messageId {messageId}");
        }
    }


    public override void OnDisconnected(DisconnectCause cause)
    {
        Log($"Disconnected from Photon: {cause}", LogLevel.Warning);
        // Optionally retry or show reconnect UI
        if (cause != DisconnectCause.DisconnectByClientLogic)
        {
            Invoke(nameof(LoginAndConnectPhoton), 2f); // Retry after delay
        }
    }
    
    [SerializeField] private bool enableLogging = true;

    [SerializeField] private LogLevel logLevel = LogLevel.Info;

    private void Log(string message, LogLevel level = LogLevel.Info)
    {
        if ((int)level <= (int)logLevel)
        {
            Debug.Log(message);
        }
    }
}
