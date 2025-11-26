// Install-Package Grpc.Net.Client -Version 2.*
// Protos compiled to namespace Riva; channels use HTTP/2 with TLS.

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Riva; // generated from riva_asr.proto
using UnityEngine;

public class RivaStreamingASR : MonoBehaviour
{
    [Header("Riva")]
    public string rivaEndpoint = "https://riva.dahvarsityai.com:443";
    public string languageCode = "en-US";
    public int micSampleRate = 16000; // Riva expects 16kHz by default
    public int chunkMs = 20;          // 20ms frames
    public Action<string> OnFinalTranscript;

    private GrpcChannel _channel;
    private SpeechRecognition.SpeechRecognitionClient _client;
    private AsyncDuplexStreamingCall<StreamingRecognizeRequest, StreamingRecognizeResponse> _call;

    private AudioClip _mic;
    private float[] _pushSamplesBuffer;
    private byte[] _pushPcmBuffer = null;        // reused PCM16 byte buffer
    private int _lastSample;
    private int _samplesPerChunk;
    private CancellationTokenSource _cts;
    public async Task StopStreaming()
    {
        if (_cts == null) return;

        try
        {
            _cts.Cancel();

            // If we have a request stream, attempt to complete it (best-effort)
            if (_call?.RequestStream != null)
            {
                try { await _call.RequestStream.CompleteAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }

            // Give background tasks a moment to finish
            await Task.Delay(50);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RivaStreamingASR] StopStreaming error: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            CleanupGrpc();
        }
    }

    public async void StartStreaming()
    {
        // If already streaming, ignore or restart
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            Debug.LogWarning("[RivaStreamingASR] StartStreaming called while already streaming.");
            return;
        }

        _cts = new CancellationTokenSource();
        // Unity WebGL cannot open raw sockets to gRPC; use this on native targets.
        try
        {
            _channel = GrpcChannel.ForAddress(rivaEndpoint, new GrpcChannelOptions
            {
                // If you use self-signed certs during dev:
                // HttpHandler = new HttpClientHandler
                // { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }
            });

            _client = new SpeechRecognition.SpeechRecognitionClient(_channel);

            // 1) Start streaming call
            _call = _client.StreamingRecognize();

            // 2) Send config first
            var cfg = new StreamingRecognitionConfig
            {
                Config = new RecognitionConfig
                {
                    LanguageCode = languageCode,
                    SampleRateHertz = micSampleRate,
                    EnableAutomaticPunctuation = true
                },
                // Interim results help you render live captions
                InterimResults = true
            };

            await _call.RequestStream.WriteAsync(new StreamingRecognizeRequest { StreamingConfig = cfg });

            // 3) Start microphone capture @ 16kHz mono
            _mic = Microphone.Start(deviceName: null, loop: true, lengthSec: 10, frequency: micSampleRate);
            _samplesPerChunk = (micSampleRate * chunkMs) / 1000;
            _pushSamplesBuffer = new float[_samplesPerChunk];
            _pushPcmBuffer = new byte[_samplesPerChunk * 2];

            // 4) Pump mic → Riva in background
            _ = Task.Run(() => PushAudioLoop(_cts.Token));
            _ = Task.Run(() => ReadTranscripts(_cts.Token));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RivaStreamingASR] StartStreaming failed: {ex}");
            _cts?.Cancel();
            CleanupGrpc();
        }
        
        /* _ = Task.Run(() => PushAudioLoop());

           // 5) Read transcripts
           _ = Task.Run(async () =>
           {
               try
               {
                   await foreach (var msg in _call.ResponseStream.ReadAllAsync())
                   {
                       foreach (var result in msg.Results)
                       {
                           var transcript = string.Join(" ", result.Alternatives.Select(a => a.Transcript));
                           bool isFinal = result.IsFinal;

                           if (isFinal && !string.IsNullOrWhiteSpace(transcript))
                           {
                               Debug.Log($"Riva ASR [final]: {transcript}");
                               OnFinalTranscript?.Invoke(transcript);
                           }
                           else
                           {
                               Debug.Log($"Riva ASR [interim]: {transcript}");
                           }
                           // TODO: UI update: live captions / final transcript
                           Debug.Log($"Riva ASR: {(isFinal ? "[final]" : "[interim]")} {transcript}");
                       }
                   }
               }
               catch (Exception ex) { Debug.LogError(ex); }
           });*/

    }

    public void EnqueuePcmFloat(float[] pcm)
    {
        short[] pcm16 = new short[pcm.Length];
        for (int i = 0; i < pcm.Length; i++)
        {
            pcm16[i] = (short)Mathf.Clamp(pcm[i] * 32767f, short.MinValue, short.MaxValue);
        }
        EnqueuePcmShort(pcm16); // your internal queue
    }

    private async Task PushAudioLoop(CancellationToken ct)
    {
        if (_call == null)
        {
            Debug.LogWarning("[RivaStreamingASR] PushAudioLoop started but call is null.");
            return;
        }

        try
        {
            var samples = _pushSamplesBuffer;
            var pcm = _pushPcmBuffer; // 16-bit PCM
            while (!ct.IsCancellationRequested)
            {
                // Safeguard the microphone
                if (_mic == null || !Microphone.IsRecording(null))
                {
                    await Task.Delay(50, ct).ConfigureAwait(false);
                    continue;
                }

                int pos = Microphone.GetPosition(null);
                int available = pos - _lastSample;

                if (available < 0) available += _mic.samples;

                if (available >= _samplesPerChunk)
                {
                    _mic.GetData(samples, _lastSample);
                    _lastSample = (_lastSample + _samplesPerChunk) % _mic.samples;

                    // float [-1,1] → int16 little-endian
                    for (int i = 0; i < samples.Length; i++)
                    {
                        int iv = Mathf.Clamp(Mathf.RoundToInt(samples[i] * 32767f), short.MinValue, short.MaxValue);
                        short s = (short)iv;
                        pcm[2 * i] = (byte)(v & 0xff);
                        pcm[2 * i + 1] = (byte)((v >> 8) & 0xff);
                    }

                    // Write to gRPC (non-blocking-ish)
                    var req = new StreamingRecognizeRequest
                    {
                        AudioContent = Google.Protobuf.ByteString.CopyFrom(pcm)
                    };

                    try
                    {
                        await _call.RequestStream.WriteAsync(req).ConfigureAwait(false);
                    }
                    catch (RpcException rpcEx)
                    {
                        Debug.LogError($"[RivaStreamingASR] RPC write error: {rpcEx.Status} - {rpcEx.Message}");
                        break;
                    }

                    /*await _call.RequestStream.WriteAsync(new StreamingRecognizeRequest
                    {
                        AudioContent = Google.Protobuf.ByteString.CopyFrom(pcm)
                    });*/
                }
                else
                {
                    // Not enough samples yet — sleep small amount (avoid busy spin)
                    try { await Task.Delay(Mathf.Max(5, chunkMs / 2), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
            try
            {
                await _call.RequestStream.WriteAsync(req).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Debug.LogWarning($"[RivaStreamingASR] RequestStream.CompleteAsync failed: {ex.Message}");
            }
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled) { }
        catch (Exception ex) { Debug.LogError(ex); }
    }

    /// <summary>
    /// Consume server responses (transcripts). Uses CancellationToken where supported.
    /// Uses ReadAllAsync(ct) to respect cancellation.
    /// </summary>
    private async Task ReadTranscripts(CancellationToken ct)
    {
        if (_call == null)
        {
            Debug.LogWarning("[RivaStreamingASR] ReadTranscripts started but call is null.");
            return;
        }

        try
        {
            // If ResponseStream supports ReadAllAsync(CancellationToken)
            await foreach (var msg in _call.ResponseStream.ReadAllAsync(ct))
            {
                if (msg == null) continue;

                foreach (var result in msg.Results)
                {
                    var transcript = string.Join(" ", result.Alternatives.Select(a => a.Transcript));
                    bool isFinal = result.IsFinal;

                    if (isFinal && !string.IsNullOrWhiteSpace(transcript))
                    {
                        Debug.Log($"Riva ASR [final]: {transcript}");
                        // raise on main thread — this is important to avoid touching Unity API from background thread
                        UnityMainThreadDispatcher.Enqueue(() => OnFinalTranscript?.Invoke(transcript));
                    }
                    else
                    {
                        Debug.Log($"Riva ASR [interim]: {transcript}");
                        // Optional: expose interim transcripts via an event (on main thread)
                        UnityMainThreadDispatcher.Enqueue(() => { /* expose interim if desired */ });
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* expected on stop */ }
        catch (RpcException rpcEx) when (rpcEx.StatusCode == Grpc.Core.StatusCode.Cancelled)
        {
            // cancellation or server-side cancel — ignore if we cancelled
            Debug.Log("[RivaStreamingASR] ReadTranscripts cancelled.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RivaStreamingASR] ReadTranscripts error: {ex}");
        }
        finally
        {
            // Ensure cleanup if response loop finishes
            CleanupGrpc();
        }
    }


    /// <summary>
    /// Cleanup lower-level gRPC resources & channel
    /// </summary>
    private void CleanupGrpc()
    {
        try
        {
            _call?.Dispose();
        }
        catch { }
        _call = null;

        try
        {
            _channel?.Dispose();
        }
        catch { }
        _channel = null;

        _client = null;
    }

    private async void OnDestroy()
    {
        _cts?.Cancel();
        await _call?.RequestStream.CompleteAsync();
        _channel?.Dispose();
        if (Microphone.IsRecording(null))
            Microphone.End(null);
    }
}