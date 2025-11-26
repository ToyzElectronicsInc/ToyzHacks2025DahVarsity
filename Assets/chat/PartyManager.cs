using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ExitGames.Client.Photon.StructWrapping;
using PlayFab.Party;
using PlayFab.ClientModels;
using PlayFab;
using UnityEngine;

public class PartyManager : MonoBehaviour
{
    public static PartyManager Instance;
    private PlayFabMultiplayerManager _network;

    public event Action<string, string> OnTextMessageReceived;

    public bool IsMuted => _network.LocalPlayer.IsMuted;

    public bool IsPartyReady { get; private set; }

    private void Awake()
    {
        Instance = this;
        _network = PlayFabMultiplayerManager.Get();
    }

    private IChatAI chatAI;

    public async Task InitializeNetwork(PlayFabNetworkConfiguration config)
    {
        if (config == null)
        {
            config = new PlayFabNetworkConfiguration();
        }

        await Task.Run(() =>
        {
            PlayFabMultiplayerManager.Get().CreateAndJoinNetwork(config);
        });
    }

    public void ToggleMute(bool isMuted)
    {
        _network.LocalPlayer.IsMuted = isMuted;
    }

    private async Task HandleIncomingText(PlayFabPlayer sender, string message)
    {
        // Try to get a human-readable display name from the PlayFabPlayer
        string displayName = GetPlayerDisplayName(sender);
        OnTextMessageReceived?.Invoke(displayName, message);

        if (chatAI == null)
        {
            Debug.LogWarning("ChatAI not initialized for PartyManager.");
            return;
        }

        // Generate smart reply suggestion (single awaited call)
        string reply = null;
        try
        {
            reply = await chatAI.GetSmartReply(message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"GetSmartReply failed: {ex.Message}");
        }
    }
    
    private string GetPlayerDisplayName(PlayFabPlayer p)
    {
        if (p == null)
            return "Unknown";

        // Try common places — adjust if your PlayFabPlayer exposes a DisplayName or similar property.
        // playfab wrapper in this workspace often keeps entity id, and PlayFabLocalPlayer or PlayFabMultiplayerManager
        // may have more friendly display-name lookup APIs; check and prefer them if present.
        try
        {
            // If PlayFabPlayer has a property like DisplayName, use it:
            var dnProp = p.GetType().GetProperty("DisplayName");
            if (dnProp != null)
            {
                var dn = dnProp.GetValue(p) as string;
                if (!string.IsNullOrEmpty(dn)) return dn;
            }
        }
        catch { /* reflection fallback ignored */ }

        // Fall back to entity id or other identifiable field
        try
        {
            var entityKeyProp = p.GetType().GetProperty("EntityKey");
            if (entityKeyProp != null)
            {
                var entityKey = entityKeyProp.GetValue(p);
                if (entityKey != null)
                {
                    // EntityKey usually has an 'Id' or similar
                    var idProp = entityKey.GetType().GetProperty("Id") ?? entityKey.GetType().GetProperty("id");
                    if (idProp != null)
                    {
                        var id = idProp.GetValue(entityKey) as string;
                        if (!string.IsNullOrEmpty(id)) return id;
                    }
                }
            }
        }
        catch { /* swallow reflection errors */ }

        // Last-resort
        return "Player";
    }

    public void SendTextMessage(string message)
    {
        if (_network == null)
        {
            Debug.LogWarning("PlayFabMultiplayerManager not initialized.");
            return;
        }

        PlayFabMultiplayerManager.Get().SendChatMessageToAllPlayers(message);
    }

    public void Shutdown()
    {
        PlayFabMultiplayerManager.Get().Suspend();
    }

    private void OnDestroy()
    {
        PlayFabMultiplayerManager.Get().Suspend();
    }
}
