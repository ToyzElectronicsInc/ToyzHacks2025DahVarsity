using System;
using System.Threading.Tasks;
using UnityEngine;

public interface IChatAI
{

    event Action<TranscriptEvent> OnFinalTranscript;
    void BeginTranscription();
    Task EndTranscription();
    Task<string> GetSmartReply(string input);
    Task<AudioClip> SpeakAsync(string text);
    void StartMediaCapture();
    Task StopMediaCapture();
    Task<string> TranscribeAsync(AudioClip clip);
}