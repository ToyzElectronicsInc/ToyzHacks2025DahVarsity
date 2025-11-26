using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using Photon.Pun;

public class AppleIntelligenceService : MonoBehaviour, IChatAI
{
    public static AppleIntelligenceService Instance { get; private set; }
    public event Action<TranscriptEvent> OnFinalTranscript; // raise when ASR yields final result
    private MediaCaptureManager mediaManager;
    private bool sessionActive = false;

    [SerializeField] private GatewayClient _gateway;
    private string _roomId => Photon.Pun.PhotonNetwork.CurrentRoom?.Name ?? "default-room";
    private string _userId => Photon.Pun.PhotonNetwork.LocalPlayer?.UserId ?? SystemInfo.deviceUniqueIdentifier;

    // add at class top:
    [SerializeField] private ChatModerationClient _chatModeration;

    // in Awake() after existing init, add:
    private void Start()
    {
        _chatModeration = FindObjectOfType<ChatModerationClient>();
        if (_chatModeration == null)
        {
            Debug.LogWarning("[AppleIntelligenceService] ChatModerationClient not found in scene. Falling back to direct Photon sends.");
        }

        _gateway = FindObjectOfType<GatewayClient>();
        if (_gateway == null) Debug.LogWarning("[AppleIntelligenceService] GatewayClient not found - will not send to gateway.");
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
        AppleSpeechBridgeWrapper.InitBridge();
        try
        {
            AppleSpeechBridge.CleanupOldAsrFiles(24 * 60 * 60);
            AppleSpeechBridge.RequestSpeechAuthorization();
        }
        catch (Exception e) { Debug.LogWarning($"[AppleIntelligenceService] init: {e}"); }
    }

    public void InjectMediaCapture(MediaCaptureManager manager) => mediaManager = manager;

#if UNITY_IOS || UNITY_STANDALONE_OSX
    [DllImport("__Internal")] private static extern void startRecognition();
    [DllImport("__Internal")] private static extern void stopRecognition();
    [DllImport("__Internal")] private static extern string predictReply(string input);
    [DllImport("__Internal")] private static extern void startSession();
    [DllImport("__Internal")] private static extern void stopSession();
    [DllImport("__Internal")] private static extern void speakText(string text);
    [DllImport("__Internal")] static extern void cleanupOldAsrTempFiles(double maxAgeSeconds);



#endif

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern IntPtr listAsrTempFiles();

    public static void LogAsrTempFiles()
    {
        IntPtr ptr = listAsrTempFiles();
        if (ptr == IntPtr.Zero) { Debug.Log("No ASR temp files."); return; }
        string raw = Marshal.PtrToStringAnsi(ptr);
        Debug.Log("[ASRTempFiles]\n" + raw);
        AppleSpeechBridge.freeNativeString(ptr);
    }
#endif

    public void BeginTranscription()
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        try { startRecognition(); } catch (Exception e) { Debug.LogError($"Apple ASR error: {e.Message}"); }
#endif
    }

    public Task EndTranscription()
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        try { stopRecognition(); } catch (Exception e) { Debug.LogError($"Apple ASR error: {e.Message}"); }
#endif
        return Task.CompletedTask;
    }

    public string GetSmartReply(string input)
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        try { return predictReply(input); } catch (Exception e) { Debug.LogError($"predictReply error: {e.Message}"); return "(predict error)"; }
#else
        return "Sorry, smart replies are not available on this platform.";
#endif
    }

    public async Task<AudioClip> SpeakAsync(string text)
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        try
        {
            AppleSpeechBridge.Speak(text);
        }
        catch (Exception e)
        {
            Debug.LogError($"SpeakAsync error: {e.Message}");
        }
        return await Task.FromResult<AudioClip>(null);
#endif
    }

    private void RaiseLocalFinal(string transcript)
    {
        var evt = new TranscriptEvent(
            text: transcript,
            senderId: PlayFabManager.Instance?.PlayFabId ?? SystemInfo.deviceUniqueIdentifier,
            senderDisplayName: PlayFabManager.Instance?.DisplayName ?? Photon.Pun.PhotonNetwork.NickName ?? "You",
            isLocal: true
        );
        OnFinalTranscript?.Invoke(evt);
    }

    public async Task<string> TranscribeAsync(AudioClip clip)
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        if (clip == null)
        {
            Debug.LogWarning("[AppleASR] No clip provided.");
            return "(no audio)";
        }

        // Extract float data from AudioClip
        // float[] samples = new float[clip.samples * clip.channels];
        // clip.GetData(samples, 0);
        byte[] wavData = WavUtils.FloatArrayToWav16(clip, 16000);

        // Send to native ASR bridge
        try
        {
            string transcript = await AppleSpeechBridge.StartTranscriptionAsync(wavData);
            RaiseLocalFinal(transcript);
            return transcript;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AppleASR] Transcription failed: {e.Message}");
            return "(transcription error)";
        }
#else
        return "(transcription unavailable)";
#endif
    }

    private byte[] ConvertFloatToWav(AudioClip clip, int targetSampleRate = 16000)
    {
        if (clip == null) return null;
        int channels = clip.channels;
        int srcRate = clip.frequency;

        float[] srcData = new float[clip.samples * channels];
        clip.GetData(srcData, 0);

        // Convert to mono by averaging channels (if more than 1)
        float[] mono = null;
        if (channels == 1) mono = srcData;
        else
        {
            int frames = clip.samples;
            mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += srcData[f * channels + c];
                mono[f] = sum / channels;
            }
        }

        // Resample if needed
        float[] resampled = mono;
        if (srcRate != targetSampleRate)
            resampled = ResampleLinear(mono, srcRate, targetSampleRate);

        return WriteWavPcm16(resampled, targetSampleRate);
    }

    private float[] ResampleLinear(float[] inSamples, int inRate, int outRate)
    {
        if (inRate == outRate) return inSamples;
        double ratio = (double)inRate / outRate;
        int outLen = (int)(inSamples.Length / ratio);
        if (outLen <= 0) return new float[0];

        float[] outSamples = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            double srcPos = i * ratio;
            int iPos = (int)Math.Floor(srcPos);
            double frac = srcPos - iPos;

            float s0 = (iPos >= 0 && iPos < inSamples.Length) ? inSamples[iPos] : 0f;
            float s1 = (iPos + 1 >= 0 && iPos + 1 < inSamples.Length) ? inSamples[iPos + 1] : 0f;
            outSamples[i] = (float)((1.0 - frac) * s0 + frac * s1);
        }
        return outSamples;
    }

    /*private byte[] WriteWavPcm16(float[] samples, int sampleRate)
    {
        if (samples == null) return new byte[0];
        int byteCount = samples.Length * 2; // 16-bit
        using (var ms = new MemoryStream(44 + byteCount))
        using (var bw = new BinaryWriter(ms))
        {
            // RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + byteCount); // file size - 8
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // subchunk1 size
            bw.Write((short)1); // PCM
            bw.Write((short)1); // mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // byte rate (sampleRate * numChannels * bytesPerSample)
            bw.Write((short)2); // block align (numChannels * bytesPerSample)
            bw.Write((short)16); // bits per sample

            // data chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(byteCount);

            // PCM data (little endian)
            foreach (var f in samples)
            {
                float clamped = Mathf.Clamp(f, -1f, 1f);
                short s = (short)Mathf.RoundToInt(clamped * 32767f);
                bw.Write(s);
            }

            return ms.ToArray();
        }
    }*/

    public async Task<string> TranscribeMicClipAsync()
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        if (mediaManager == null) return "(no media manager)";
        float[] micData = mediaManager.GetMicData();
        if (micData == null || micData.Length == 0) return "(no mic data)";


        AudioClip clip = AudioClip.Create("MicClip", micData.Length, 1, 16000, false);
        clip.SetData(micData, 0);
        return await TranscribeAsync(clip);
#else
        return "(unsupported platform)";
#endif
    }

    public void ConfigureVoice(string languageCode, float rate, float pitch, float volume)
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        try
        {
            AppleSpeechBridge.SetVoice(languageCode);
            AppleSpeechBridge.SetRatePitchVolume(rate, pitch, volume);
        }
        catch (Exception e)
        {
            Debug.LogError($"ConfigureVoice error: {e.Message}");
        }
#endif
    }

    public void StartMediaCapture()
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        if (!sessionActive)
        {
            try
            {
                startSession();
                sessionActive = true;
                mediaManager?.StartCapture();
            }
            catch (Exception e) { Debug.LogError($"StartMediaCapture error: {e.Message}"); }
        }
#endif
    }

    public Task StopMediaCapture()
    {
#if UNITY_IOS || UNITY_STANDALONE_OSX
        if (sessionActive)
        {
            try
            {
                stopSession();
                mediaManager?.StopCapture();
            }
            catch (Exception e) { Debug.LogError($"StopMediaCapture error: {e.Message}"); }
        }
#endif
    }

    public async void OnTranscriptionReceived(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        ChatFrontEnd.instance?.AddMessageToChat(text);

        // If moderation client exists, prefer it (it will handle publishing).
        if (_chatModeration != null)
        {
            _ = _chatModeration.HandleVoiceTranscriptAsync(text);
            return;
        }

        try
        {
            if (_gateway != null)
            {
                var audioUrl = null as string; // set if you have audio bytes
                var messageId = await _gateway.SendMessageToGatewayAsync(_roomId, _userId, text, audioUrl);
                if (messageId != null) return; // gateway accepted — authoritative
                Debug.LogWarning("[AppleIntelligenceService] gateway send failed; falling back to Photon.");
            }

            // local UI preview using server-assigned opt. For simplicity create payload first:
            var payload = ChatMessagePayload.Create(
                senderId: Photon.Pun.PhotonNetwork.LocalPlayer?.UserId,
                senderName: Photon.Pun.PhotonNetwork.LocalPlayer?.NickName,
                roomId: Photon.Pun.PhotonNetwork.CurrentRoom?.Name,
                text: text,
                mod: null,
                audioUrl: null
            );

            // Add to UI as pending/optimistic (if ChatFrontEnd supports it)
            //TODO ChatFrontEnd.instance?.MarkLocalMessageAsPending(payload.MessageId, payload.Text);

            // Send
            ChatFrontEnd.instance?.MarkLocalMessageAsPending(payload.MessageId, payload.Text);
            PhotonManager.Instance.SendChatMessage(payload);

        }
        catch (Exception e)
        {
            Debug.LogError($"SendMessage failed: {e.Message}");
        }

    }

    private void RaiseRemoteFinal(string transcript, string remoteUserId, string remoteDisplayName)
    {
        var evt = new TranscriptEvent(
            text: transcript,
            senderId: remoteUserId,
            senderDisplayName: remoteDisplayName,
            isLocal: false
        );
        OnFinalTranscript?.Invoke(evt);
    }

    public static void LogAsrTempFiles()
    {
        var files = AppleSpeechBridge.GetNativeStringList();
        if (files == null || files.Count == 0)
        {
            Debug.Log("No ASR temp files.");
            return;
        }

        Debug.Log("[ASRTempFiles]\n" + string.Join("\n", files));
        foreach (var f in files) Debug.Log("  " + f);
    }

    // This is the method that the native callback will call (wired earlier)
    public async void HandleVoiceTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;

        // Optionally show in UI as pending
        ChatFrontEnd.instance?.AddMessageToChat(transcript, pending: true);

        // Ask moderation pipeline for final payload (may be null if blocked)
        ChatMessagePayload payload = null;
        try
        {
            payload = await _chatModeration.HandleVoiceTranscriptAsync(transcript);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AppleIntelligenceService] moderation pipeline failed: {ex}");
            // fallback: create a basic payload (or abort). We'll abort to be conservative.
            ChatFrontEnd.instance?.MarkLocalMessageAsFailed(transcript);
            return;
        }

        if (payload == null)
        {
            // Blocked or user cancelled
            ChatFrontEnd.instance?.MarkLocalMessageAsFailed(transcript);
            return;
        }

        // Optional: upload audio and attach audio URL
        try
        {
            // If you have recorded the WAV bytes for this transcript, upload them and set payload.AudioUrl
            // byte[] wavBytes = ...; // fetch from your capture manager
            // payload.AudioUrl = await _gateway.UploadAudioAsync(wavBytes);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AppleIntelligenceService] audio upload failed: {ex}");
        }

        // Send via Photon (centralized)
        PhotonManager.Instance.SendChatMessage(payload);

        // Optionally notify gateway for authoritative storage & server-side moderation
        try
        {
            if (_gateway != null)
            {
                _ = _gateway.SendMessageToGatewayAsync(payload); // fire-and-forget
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AppleIntelligenceService] Gateway send failed: {ex}");
        }
    }
}