using System;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Riva; // generated from riva_tts.proto
using UnityEngine;

public class RivaTtsClient : MonoBehaviour
{
    public string rivaEndpoint = "https://riva.dahvarsityai.com:443";
    public string voiceName = "English-US.Female-1"; // pick a deployed voice
    public int sampleRate = 22050;
    private GrpcChannel _channel;
    private TextToSpeech.TextToSpeechClient _client;

    async void Start()
    {
        _channel = GrpcChannel.ForAddress(rivaEndpoint);
        _client = new TextToSpeech.TextToSpeechClient(_channel);

        var req = new SynthesizeSpeechRequest
        {
            Text = "Welcome to DahVarsity. Let's build your superpowers.",
            Voice = new VoiceSelectionParams { Name = voiceName, LanguageCode = "en-US" },
            AudioEncoding = AudioEncoding.LINEAR_PCM,
            SampleRateHz = sampleRate
        };
        var res = await _client.SynthesizeAsync(req);
        var pcm = res.Audio;
        // TODO: create an AudioClip and play. PCM is 16-bit LE; convert to float [-1,1].
        PlayPcmAsUnityClip(pcm.ToByteArray(), sampleRate);
    }

    void PlayPcmAsUnityClip(byte[] pcmLe, int sr)
    {
        if (pcmLe == null || pcmLe.Length == 0) return;

        int samples = pcmLe.Length / 2;
        float[] buffer = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            short v = (short)(pcmLe[2 * i] | (pcmLe[2 * i + 1] << 8));
            buffer[i] = v / 32768f;
        }
        var clip = AudioClip.Create("riva-tts", samples, 1, sr, false);
        clip.SetData(buffer, 0);
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.Play();
    }
    
    private async Task Awake()
    {
        _channel = GrpcChannel.ForAddress(rivaEndpoint);
        _client = new TextToSpeech.TextToSpeechClient(_channel);
        await SpeakAsync("Welcome to DahVarsity. Let's build your superpowers.");
    }

    public async Task SpeakAsync(string text)
    {
        var req = new SynthesizeSpeechRequest
        {
            Text = text,
            Voice = new VoiceSelectionParams { Name = voiceName, LanguageCode = "en-US" },
            AudioEncoding = AudioEncoding.LINEAR_PCM,
            SampleRateHz = sampleRate
        };
        var res = await _client.SynthesizeAsync(req);
        PlayPcmAsUnityClip(res.Audio.ToByteArray(), sampleRate);
    }

    private void OnDestroy() => _channel?.Dispose();
}