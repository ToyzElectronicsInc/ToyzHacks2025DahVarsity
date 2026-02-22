using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class PlayFabAvatarSync : MonoBehaviour
{
    [Header("PlayFab Settings")]
    public string titleId = "18685E";

    [Header("References")]
    public MetaPersonLoader avatarLoader;

    [Header("Manual Testing")]
    public string manualAvatarUrl = "";
    public bool useManualUrl = true;

    void Start()
    {
        if (useManualUrl && !string.IsNullOrEmpty(manualAvatarUrl))
        {
            Debug.Log("Loading avatar from manual URL...");
            if (avatarLoader != null)
                avatarLoader.LoadAvatarFromUrl(manualAvatarUrl);
        }
    }

    public void LoadAvatarFromPlayFab(string sessionTicket)
    {
        StartCoroutine(GetUserDataCoroutine(sessionTicket));
    }

    IEnumerator GetUserDataCoroutine(string sessionTicket)
    {
        string url = $"https://{titleId}.playfabapi.com/Client/GetUserData";
        string json = "{\"Keys\":[\"avatarUrl\"]}";

        var request = new UnityWebRequest(url, "POST");
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("X-Authorization", sessionTicket);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log($"PlayFab response: {request.downloadHandler.text}");
            // Parse and load avatar URL from response
        }
        else
        {
            Debug.LogError($"PlayFab error: {request.error}");
        }
    }
}