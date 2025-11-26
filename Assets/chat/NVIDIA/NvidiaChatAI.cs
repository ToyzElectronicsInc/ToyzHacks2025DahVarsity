using System;
using UnityEngine;
using System.Threading.Tasks;

public class NvidiaChatAI : IChatAI
{
    public static NvidiaChatAI Instance { get; private set; }
    public event Action<TranscriptEvent> OnFinalTranscript;
    private bool useMaxineAudio = true;

    private NvidiaLLMClient llmClient;
    private RivaTranscriptionClient rivaClient;
    private RivaStreamingASR streamingASR;
    private RivaTtsClient ttsClient;

    private GatewayClient _gateway;
    private string _roomId => Photon.Pun.PhotonNetwork.CurrentRoom?.Name ?? "default-room";
    private string _userId => Photon.Pun.PhotonNetwork.LocalPlayer?.UserId ?? SystemInfo.deviceUniqueIdentifier;

    private ChatModerationClient _chatModeration;

    private TranscriptionMode mode = TranscriptionMode.Streaming;
    private AudioClip micClip;
    private bool isRecording;
    public enum TranscriptionMode
    {
        ClipBased,
        Streaming
    }

    private NvidiaChatAI() { }

    static NvidiaChatAI()
    {
        MaxineAudioFX.NvAFX_Initialize();
    }

    // public void BeginTranscription()
    // {
    //     micClip = Microphone.Start(null, false, 10, 16000);
    //     isRecording = true;
    // }

    // public async void EndTranscription()
    // {
    //     if (!isRecording) return;
    //     Microphone.End(null);
    //     isRecording = false;

    //     byte[] audioData = WavUtility.FromAudioClip(micClip); // convert to WAV
    //     string transcript = await rivaClient.TranscribeAsync(audioData);
    //     ChatFrontEnd.instance.OnTranscriptionReceived(transcript);
    // }

    public void SetTranscriptionMode(TranscriptionMode selectedMode)
    {
        mode = selectedMode;
    }

    private void InitModeration()
    {
        if (_chatModeration == null) _chatModeration = UnityEngine.Object.FindObjectOfType<ChatModerationClient>();
        if (_chatModeration == null) Debug.LogWarning("[NvidiaChatAI] ChatModerationClient not found. Messages will be sent directly.");
    }

    private void InitializeModerationGateway()
    {
        if (_gateway == null) _gateway = UnityEngine.Object.FindObjectOfType<GatewayClient>();
    }

    public static void Initialize(NvidiaLLMClient llm, RivaTtsClient tts, RivaStreamingASR streaming, RivaTranscriptionClient transcriber)
    {
        if (Instance == null)
        {
            Instance = new NvidiaChatAI
            {
                llmClient = llm,
                ttsClient = tts,
                streamingASR = streaming,
                rivaClient = transcriber
            };
        }
    }

    public void EnableMaxine(bool enabled)
    {
        useMaxineAudio = enabled;
    }

    public void BeginTranscription()
    {
        if (mode == TranscriptionMode.ClipBased)
        {
            micClip = Microphone.Start(null, false, 10, 16000);
            isRecording = true;
        }
        else if (mode == TranscriptionMode.Streaming)
        {
            streamingASR.OnFinalTranscript += OnStreamingFinalHandler;
            streamingASR.StartStreaming(); // You may need to expose this method
        }
    }

    public async Task EndTranscription()
    {
        if (mode == TranscriptionMode.ClipBased)
        {
            if (!isRecording) return;
            Microphone.End(null);
            isRecording = false;

            float[] raw = new float[micClip.samples];
            micClip.GetData(raw, 0);

            if (useMaxineAudio)
            {
                float[] enhanced = new float[raw.Length];
                MaxineAudioFX.NvAFX_ProcessAudio(raw, enhanced, raw.Length);
                raw = enhanced;
            }

            byte[] wavData = WavUtils.FloatArrayToWav16(raw, 16000);
            string transcript = await rivaClient.TranscribeAsync(wavData);
            ChatFrontEnd.instance.OnTranscriptionReceived(transcript);

            string reply = GetSmartReply(transcript);
            AudioClip response = await ttsClient.GenerateSpeechClip(reply);
            PlayClip(response);
        }
        else if (mode == TranscriptionMode.Streaming)
        {
            streamingASR.OnFinalTranscript -= OnStreamingFinalHandler;
            await streamingASR.StopStreaming(); // You may need to expose this method
        }
    }

    /*public static byte[] ConvertFloatToWav(float[] audioData, int sampleRate = 16000)
    {
        int samples = audioData.Length;
        short[] pcm16 = new short[samples];

        // Convert float [-1.0, 1.0] to PCM16
        for (int i = 0; i < samples; i++)
        {
            pcm16[i] = (short)Mathf.Clamp(audioData[i] * 32767f, short.MinValue, short.MaxValue);
        }

        int byteRate = sampleRate * 2; // 16-bit mono
        int dataSize = samples * 2;
        int headerSize = 44;

        using (MemoryStream stream = new MemoryStream(headerSize + dataSize))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            // RIFF header
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(headerSize + dataSize - 8);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // PCM header size
            writer.Write((short)1); // PCM format
            writer.Write((short)1); // mono
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)2); // block align
            writer.Write((short)16); // bits per sample

            // data chunk
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            // Write PCM data
            foreach (short sample in pcm16)
            {
                writer.Write(sample);
            }

            return stream.ToArray();
        }
    }*/

    private void OnStreamingFinalHandler(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;
        RaiseLocalFinal(transcript); // converts to TranscriptEvent and fires OnFinalTranscript
    }

    private async void HandleLiveTranscript(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return;
        RaiseLocalFinal(transcript);

        ChatFrontEnd.instance.OnTranscriptionReceived(transcript);


        //InitModeration();
        InitializeModerationGateway();
        if (_gateway != null)
        {
            // no audioBytes in this streaming path (unless you capture them)
            var messageId = await _gateway.SendMessageToGatewayAsync(_roomId, _userId, transcript, null);
            if (messageId == null)
            {
                Debug.LogWarning("[NvidiaChatAI] gateway send failed, fallback to Photon.");
                var payload = ChatMessagePayload.Create(
                    senderId: Photon.Pun.PhotonNetwork.LocalPlayer?.UserId,
                    senderName: Photon.Pun.PhotonNetwork.LocalPlayer?.NickName,
                    roomId: Photon.Pun.PhotonNetwork.CurrentRoom?.Name,
                    text: transcript,
                    mod: null,
                    audioUrl: null
                );
                PhotonManager.Instance.SendChatMessage(payload);

            }
        }
        else
        {
            PhotonManager.Instance.SendMessage(transcript);
        }
        string reply = GetSmartReply(transcript);
        AudioClip clip = await ttsClient.GenerateSpeechClip(reply);
        PlayClip(clip);
    }

    public Task<string> GetSmartReply(string input)
    {
        return Task.Run(() => llmClient.GetSmartReply(input));
    }

    public async Task<AudioClip> SpeakAsync(string text)
    {
        AudioClip clip = await ttsClient.GenerateSpeechClip(text);
        PlayClip(clip);
        return clip;
    }

    public async Task<string> TranscribeAsync(AudioClip clip)
    {
        return await rivaClient.TranscribeClipAsync(clip);
    }

    public void OnRemoteVoiceFrame(float[] pcm)
    {
        float[] enhanced = new float[pcm.Length];
        MaxineAudioFX.NvAFX_ProcessAudio(pcm, enhanced, pcm.Length);
        streamingASR.EnqueuePcmFloat(enhanced);
    }

    public void StartMediaCapture()
    {
        BeginTranscription();
    }

    private void RaiseLocalFinal(string transcript)
    {
        var evt = new TranscriptEvent(
            text: transcript,
            senderId: PlayFabManager.Instance?.PlayFabId ?? SystemInfo.deviceUniqueIdentifier,
            senderDisplayName: PlayFabManager.Instance?.DisplayName ?? PhotonNetwork.NickName ?? "You",
            isLocal: true
        );
        OnFinalTranscript?.Invoke(evt);
    }

    // Example when PartyManager or remote ASR provides transcript + sender
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

    public async Task StopMediaCapture()
    {
        await EndTranscription();
    }

    private void PlayClip(AudioClip clip)
    {
        var src = new GameObject("TTSPlayer").AddComponent<AudioSource>();
        src.clip = clip;
        src.Play();
        UnityEngine.Object.Destroy(src.gameObject, clip.length + 1f);
    }
    



    // public async Task<AudioClip> TranscribeAsync(AudioClip clip)
    // {
    //     string transcript = await rivaClient.TranscribeClipAsync(clip);
    //     ChatFrontEnd.instance.OnTranscriptionReceived(transcript);
    //     AudioClip spoken = await ttsClient.GenerateSpeechClip(transcript);
    //     PlayClip(spoken);
    //     return spoken;
    // }
}
