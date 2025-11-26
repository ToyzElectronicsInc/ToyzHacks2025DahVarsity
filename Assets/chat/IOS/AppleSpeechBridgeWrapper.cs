using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class AppleSpeechBridgeWrapper
{

    // Keep the delegate alive for the app lifetime
    private static AppleSpeechBridge.TranscriptionCallback s_callback;

    /// <summary>
    /// Initializes the bridge by registering the managed callback with native.
    /// Call this once at startup (e.g., in Awake of AppleIntelligenceService).
    /// </summary>
    public static void InitBridge()
    {
        if (s_callback != null) return; // already initialized
        s_callback = OnNativeTranscription;
        try{
            AppleSpeechBridge.registerTranscriptionCallback(s_callback);
            Debug.Log("[AppleSpeechBridgeWrapper] Callback registered with native plugin.");
        }
        catch(Exception ex)
        {
            Debug.LogError("[AppleSpeechBridgeWrapper] Failed to register callback with native plugin: " + ex);
        }
    }

    // The native callback is invoked on some thread (not necessarily main).
    // Marshal.PtrToStringAnsi copies into managed memory, then call freeNativeString.
    private static void OnNativeTranscription(IntPtr requestIdPtr, IntPtr transcriptPtr, IntPtr errorPtr)
    {
        string requestId = null;
        string transcript = null;
        string error = null;

        try
        {
            if (requestIdPtr != IntPtr.Zero) requestId = Marshal.PtrToStringAnsi(requestIdPtr);
            if (transcriptPtr != IntPtr.Zero) transcript = Marshal.PtrToStringAnsi(transcriptPtr);
            if (errorPtr != IntPtr.Zero) error = Marshal.PtrToStringAnsi(errorPtr);
        }
        finally
        {
            if (requestIdPtr != IntPtr.Zero) AppleSpeechBridge.freeNativeString(requestIdPtr);
            if (transcriptPtr != IntPtr.Zero) AppleSpeechBridge.freeNativeString(transcriptPtr);
            if (errorPtr != IntPtr.Zero) AppleSpeechBridge.freeNativeString(errorPtr);
        }

        // Now process on Unity main thread (if needed)
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            if (!string.IsNullOrEmpty(error))
                Debug.LogError($"ASR error: {error}");
            else
                AppleIntelligenceService.Instance?.OnTranscriptionReceived(transcript);
        });
    }
}