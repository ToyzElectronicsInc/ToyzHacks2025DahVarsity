using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class ASRStartupCleanup : MonoBehaviour
{
    // DllImport for the native cleanup helper (implemented earlier)
    #if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void cleanupOldAsrTempFiles(double maxAgeSeconds);
    #endif

    // Called automatically when the game loads (before first scene)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void OnBeforeSceneLoad()
    {
        // Create a GameObject to host the cleanup MonoBehaviour so logs show up nicely (optional)
        var go = new GameObject("ASRStartupCleanup");
        DontDestroyOnLoad(go);
        go.AddComponent<ASRStartupCleanup>();
    }

    private void Awake()
    {
        // Delete ASR temp files older than 24 hours (24*60*60 seconds)
        try
        {
#if UNITY_IOS && !UNITY_EDITOR
            double maxAge = 24 * 60 * 60;
            cleanupOldAsrTempFiles(maxAge);
            Debug.Log("[ASRStartupCleanup] Requested native cleanup of ASR temp files older than 24 hours.");
#else
            Debug.Log("[ASRStartupCleanup] Skipping native cleanup (not running on iOS device).");
#endif
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ASRStartupCleanup] Cleanup failed: {ex}");
        }

        // Optional: do other init if you want (e.g., request speech auth)
#if UNITY_IOS && !UNITY_EDITOR
        try
        {
            AppleSpeechBridge.RequestSpeechAuthorization();
            Debug.Log("[ASRStartupCleanup] Requested speech authorization prompt (if needed).");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ASRStartupCleanup] RequestSpeechAuthorization failed: {e}");
        }
#endif
    }
}
