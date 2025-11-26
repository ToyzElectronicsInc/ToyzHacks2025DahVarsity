using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json; // add this (install NewtonSoft package)
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class GatewayClient : MonoBehaviour
{
    [Tooltip("Gateway base URL (e.g. https://gateway.example.com)")]
    public string GatewayBaseUrl = "https://gateway.example.com";

    // If you have an auth token provider, inject/use it here
    public Func<Task<string>> GetAuthTokenAsync = async () => null; // TODO: replace with real provider

    private readonly HttpClient _httpClient;

    public GatewayClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // Parameterless Unity-friendly constructor (for MonoBehaviour usage)
    public GatewayClient()
    {
        // if not injected, create a default instance
        _httpClient = new HttpClient { BaseAddress = new Uri(GatewayBaseUrl) };
    }

    // Upload raw WAV bytes (POST /api/messages/upload-audio) -> returns { blobUrl }
    public async Task<string> UploadAudioAsync(byte[] wavData)
    {
        if (wavData == null || wavData.Length == 0) return null;
        var url = $"{GatewayBaseUrl.TrimEnd('/')}/api/messages/upload-audio";
        using var uw = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        uw.uploadHandler = new UploadHandlerRaw(wavData);
        uw.downloadHandler = new DownloadHandlerBuffer();
        uw.SetRequestHeader("Content-Type", "application/octet-stream");

        var token = await GetAuthTokenAsync();
        if (!string.IsNullOrEmpty(token)) uw.SetRequestHeader("Authorization", $"Bearer {token}");

        var op = uw.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (uw.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("[GatewayClient] UploadAudio failed: " + uw.error);
            return null;
        }

        var txt = uw.downloadHandler.text;
        // expect { blobUrl: "..." }
        try
        {
            var resp = JsonUtility.FromJson<UploadResponse>(txt);
            return resp?.blobUrl;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GatewayClient] parse upload response failed: " + ex);
            return null;
        }
    }

    // Send message (POST /api/messages/send) -> returns { messageId }
    public async Task<string> SendMessageToGatewayAsync(
        string roomId, 
        string userId, 
        string text, 
        string audioUrl = null,
        string senderName = null, 
        string messageId = null, 
        object moderationMeta = null, 
        DateTime? timestamp = null)
    {
        /*var url = $"{GatewayBaseUrl}/api/messages/send";
        var dto = new SendDto { RoomId = roomId, UserId = userId, Text = text, AudioUrl = audioUrl };
        var json = JsonUtility.ToJson(dto);

        using var uw = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
        uw.uploadHandler = new UploadHandlerRaw(bodyRaw);
        uw.downloadHandler = new DownloadHandlerBuffer();
        uw.SetRequestHeader("Content-Type", "application/json");

        var token = await GetAuthTokenAsync();
        if (!string.IsNullOrEmpty(token)) uw.SetRequestHeader("Authorization", $"Bearer {token}");

        var op = uw.SendWebRequest();
        while (!op.isDone) await Task.Yield();

        if (uw.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("[GatewayClient] SendMessageToGateway failed: " + uw.error + " body: " + uw.downloadHandler.text);
            return null;
        }

        try
        {
            var resp = JsonUtility.FromJson<SendResponse>(uw.downloadHandler.text);
            return resp?.messageId;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GatewayClient] parse send response failed: " + ex);
            return null;
        }*/
        if (string.IsNullOrWhiteSpace(roomId)) throw new ArgumentNullException(nameof(roomId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentNullException(nameof(userId));
        if (string.IsNullOrWhiteSpace(text)) text = "";
        var dto = new
        {
            messageId = messageId ?? Guid.NewGuid().ToString("N"),
            senderId = userId,
            senderName = senderName,
            roomId = roomId,
            text = text,
            audioUrl = audioUrl,
            timestamp = timestamp?.ToUniversalTime().ToString("o"),
            moderation = moderationMeta
        };

        try
        {
            // Ensure full absolute url (server expects /api/messages/send or /api/messages depending on your server)
            // Use /api/messages/send if your server route is that; adjust if your server uses a different path.
            var url = $"{GatewayBaseUrl.TrimEnd('/')}/api/messages/send";

            // Serialize using Newtonsoft (Unity-friendly)
            var payloadJson = JsonConvert.SerializeObject(dto);
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            // attach auth header if available
            var token = await MaybeGetAuthTokenAsync();
            if (!string.IsNullOrEmpty(token))
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            else
                _httpClient.DefaultRequestHeaders.Authorization = null;

            // Send
            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var txt = await response.Content.ReadAsStringAsync();
                Debug.LogWarning($"[GatewayClient] SendMessageToGatewayAsync failed: {(int)response.StatusCode} {response.StatusCode} - {txt}");
                return null;
            }

            var respText = await response.Content.ReadAsStringAsync();
            try
            {
                var result = JsonConvert.DeserializeObject<GatewayResponseDto>(respText);
                return result?.MessageId;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GatewayClient] parse send response failed: {ex} ; raw: {respText}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GatewayClient] exception when sending message: {ex}");
            return null;
        }
    }

    // Helper to safely get auth token
    private async Task<string> MaybeGetAuthTokenAsync()
    {
        try
        {
            if (GetAuthTokenAsync == null) return null;
            return await GetAuthTokenAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GatewayClient] GetAuthTokenAsync failed: " + ex);
            return null;
        }
    }
}

[Serializable] public class UploadResponse { public string blobUrl; }
//[Serializable] public class SendDto { public string RoomId; public string UserId; public string Text; public string AudioUrl; }
//[Serializable] public class SendResponse { public string messageId; }
public class GatewayResponseDto
{
    [JsonProperty("messageId")]
    public string MessageId { get; set; }
}
