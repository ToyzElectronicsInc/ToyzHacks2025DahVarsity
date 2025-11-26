using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public static class AppleSpeechBridge
{
    private const string Dll = "__Internal";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void speakText(string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void setVoice(string languageCode);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void setRatePitchVolume(float rate, float pitch, float volume);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr getAvailableVoices();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void freeNativeString(IntPtr s);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void speakWithVoice(string text, string voiceID);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern string transcribeWav(byte[] wavData, int length);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void transcribeWavAsync(byte[] wavData, int length, string requestId);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void registerTranscriptionCallback(NativeTranscriptionCallback cb);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void requestSpeechAuthorization();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr listAsrTempFiles();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr getSomeList(out int count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void freeStringArray(IntPtr arr, int count);



    public delegate void NativeTranscriptionCallback(IntPtr requestIdPtr, IntPtr transcriptPtr, IntPtr errorPtr);
    //private static NativeTranscriptionCallback _managedCallback = OnNativeTranscription;
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingTranscriptions = new ConcurrentDictionary<string, TaskCompletionSource<string>>();



    public static void Speak(string text) => speakText(text);
    public static void SetVoice(string languageCode) => setVoice(languageCode);
    public static void SetRatePitchVolume(float rate, float pitch, float volume) => setRatePitchVolume(rate, pitch, volume);
    public static void SpeakWithVoice(string text, string voiceID) => speakWithVoice(text, voiceID);
    public static void RequestSpeechAuthorization() => requestSpeechAuthorization();

    public struct AppleVoiceInfo
    {
        public string Identifier;
        public string Name;
        public string Language;
    }

    /// <summary>
    /// Returns an ordered list of ASR temp file paths as returned by native listAsrTempFiles().
    /// Native must return a heap-allocated (strdup/malloc) UTF-8 C string; this function always calls freeNativeString(ptr).
    /// </summary>
    public static List<string> GetAsrTempFiles()
    {
        IntPtr ptr = listAsrTempFiles();
        var result = new List<string>();
        if (ptr == IntPtr.Zero) return result;
        try
        {
            string raw = Marshal.PtrToStringAnsi(ptr) ?? "";
            foreach (var p in raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(p.Trim());
            return result;
        }
        finally { freeNativeString(ptr); }
    }

    // Start async transcription: returns transcript string (or throws on error)
    public static Task<string> StartTranscriptionAsync(byte[] wav)
    {
        if (wav == null || wav.Length == 0) return Task.FromResult("(no audio)");

        string requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>();
        _pendingTranscriptions[requestId] = tcs;

        try
        {
            transcribeWavAsync(wav, wav.Length, requestId);
        }
        catch (Exception ex)
        {
            // cleanup on failure
            _pendingTranscriptions.TryRemove(requestId, out _);
            tcs.SetException(ex);
        }

        return tcs.Task;
    }

    // Managed callback invoked by native code.
    // Note: native code passes strdup'd C strings but we declared native to free immediately after the call,
    // so we can marshal IntPtr -> string safely.
    [AOT.MonoPInvokeCallback(typeof(NativeTranscriptionCallback))]
    private static void OnNativeTranscription(IntPtr reqPtr, IntPtr transcriptPtr, IntPtr errorPtr)
    {
        string req = null, transcript = null, error = null;
        try
        {
            if (reqPtr != IntPtr.Zero) req = Marshal.PtrToStringAnsi(reqPtr);
            if (transcriptPtr != IntPtr.Zero) transcript = Marshal.PtrToStringAnsi(transcriptPtr);
            if (errorPtr != IntPtr.Zero) error = Marshal.PtrToStringAnsi(errorPtr);
        }
        finally
        {
            if (reqPtr != IntPtr.Zero) freeNativeString(reqPtr);
            if (transcriptPtr != IntPtr.Zero) freeNativeString(transcriptPtr);
            if (errorPtr != IntPtr.Zero) freeNativeString(errorPtr);
        }

        if (string.IsNullOrEmpty(req))
        {
            Debug.LogWarning("[AppleSpeechBridge] empty requestId in native callback");
            return;
        }

        if (_pendingTranscriptions.TryRemove(req, out var tcs))
        {
            if (!string.IsNullOrEmpty(error)) tcs.SetException(new Exception(error));
            else tcs.SetResult(transcript ?? "");
        }
        else
        {
            Debug.LogWarning($"[AppleSpeechBridge] no pending transcription for {req}");
        }
    }  

    public static List<string> GetNativeStringList()
    {
        IntPtr arrPtr = IntPtr.Zero; int count = 0;
        var list = new List<string>();
        try
        {
            arrPtr = getSomeList(out count);
            if (arrPtr == IntPtr.Zero || count <= 0) return list;
            for (int i = 0; i < count; i++)
            {
                IntPtr strPtr = Marshal.ReadIntPtr(arrPtr, i * IntPtr.Size);
                list.Add(strPtr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(strPtr));
            }
            return list;
        }
        finally
        {
            if (arrPtr != IntPtr.Zero) freeStringArray(arrPtr, count);
        }
    }
}
