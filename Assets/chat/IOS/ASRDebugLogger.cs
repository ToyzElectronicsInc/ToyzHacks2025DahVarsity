using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class ASRDebugLogger : MonoBehaviour
{
    // Keep a registry of outstanding request IDs for debug
    private readonly HashSet<string> _outstandingRequests = new HashSet<string>();

    // Example usage: call this with WAV bytes (PCM16 WAV) or call the helper that converts an AudioClip
    public async Task<string> StartTranscriptionWithLoggingAsync(byte[] wavBytes)
    {
        if (wavBytes == null || wavBytes.Length == 0)
        {
            Debug.LogWarning("[ASRDebugLogger] No wav data provided.");
            return "(no-audio)";
        }

        // The AppleSpeechBridge.StartTranscriptionAsync creates a GUID-based requestId internally.
        // We'll log before and after and rely on the native temp filename convention: asr_<requestId>.wav
        string requestId = Guid.NewGuid().ToString("N"); // we use our own id here so we can log consistent filename "asr_<id>.wav"
        string expectedFilename = $"asr_{requestId}.wav";
        Debug.Log($"[ASRDebugLogger] (pre-request) issuing transcription requestId={requestId}. Expected temp file name: {expectedFilename}");

        // NOTE: Apple's StartTranscriptionAsync implementation earlier used its own GUID if you call it directly.
        // To correlate, we need to call the lower-level transcribe method which accepts a requestId. If your bridge
        // only exposes StartTranscriptionAsync(byte[]), adapt it to allow passing a requestId. For now, we call the StartTranscriptionAsync
        // and log using the thrown-away requestId above — it's primarily for your local tracing.
        //
        // If you updated the bridge to expose a method that accepts requestId, use that to keep logs exact.
        try
        {
            // If you added an overload AppleSpeechBridge.StartTranscriptionAsync(byte[], string requestId) -> Task<string>
            // use that here. Otherwise, call the existing API and rely on Unity-managed logs to trace.
#if UNITY_IOS && !UNITY_EDITOR
            // Example if you have modified the bridge to accept requestId:
            // string transcript = await AppleSpeechBridge.StartTranscriptionAsync(wavBytes, requestId);

            // Otherwise call the regular API:
            string transcript = await AppleSpeechBridge.StartTranscriptionAsync(wavBytes);

            Debug.Log($"[ASRDebugLogger] (callback) transcription complete for requestId={requestId}. Transcript: {transcript}");
            return transcript;
#else
            Debug.Log("[ASRDebugLogger] Skipping transcription (not on iOS device).");
            await Task.Delay(10);
            return "(unsupported)";
#endif
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ASRDebugLogger] Transcription failed for requestId={requestId}: {ex.Message}");
            return "(error)";
        }
    }

    // Helper to convert an AudioClip to WAV then start transcription (calls your AppleIntelligenceService helper)
    public async Task<string> TranscribeClipWithLogging(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogWarning("[ASRDebugLogger] Null clip");
            return "(no-audio)";
        }

        // Use your service helper that resamples and creates a WAV byte[] (we used ConvertFloatToWav earlier)
        byte[] wav = WavUtils.FloatArrayToWav16(clip, 16000);
        return await StartTranscriptionWithLoggingAsync(wav);
    }
}
