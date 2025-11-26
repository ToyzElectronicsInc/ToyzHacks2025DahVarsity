using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple AudioSource pool for playing TTS audio clips with minimal allocation.
/// </summary>
[RequireComponent(typeof(AudioSource))] // optional
public class TtsPool : MonoBehaviour
{
    [SerializeField] private int poolSize = 3;
    private AudioSource[] sources;
    private int rr = 0;

    private void Awake()
    {
        sources = new AudioSource[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f; // 2D TTS
            // tune default volume, pitch, etc.
            sources[i] = src;
        }
    }

    public void Play(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        var src = sources[(rr++) % sources.Length];
        src.Stop();
        src.clip = clip;
        src.volume = volume;
        src.Play();
    }

    // Optionally: Enqueue for delayed playback or prioritization.
}
