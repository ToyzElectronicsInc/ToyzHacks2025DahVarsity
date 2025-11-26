using UnityEngine;

/// <summary>
/// Small helper utilities for creating AudioClips from PCM16 bytes or float arrays.
/// IMPORTANT: AudioClip.Create and clip.SetData must be called on the Unity main thread.
/// </summary>
public static class AudioClipUtils
{
    /// <summary>
    /// Convert PCM16 little-endian byte[] (mono) to a Unity AudioClip.
    /// Caller must call this on the Unity main thread.
    /// </summary>
    public static AudioClip CreateClipFromPcm16(byte[] pcmLe, int sampleRate, string name = "clip")
    {
        if (pcmLe == null || pcmLe.Length == 0) return null;
        // Convert to float[] using WavUtils (centralized)
        float[] floats = WavUtils.Pcm16LeToFloatArray(pcmLe);
        if (floats == null || floats.Length == 0) return null;

        var clip = AudioClip.Create(name, floats.Length, 1, sampleRate, false);
        clip.SetData(floats, 0);
        return clip;
    }

    /// <summary>
    /// Create an AudioClip directly from a float[] PCM buffer (mono).
    /// Caller must call this on the Unity main thread.
    /// </summary>
    public static AudioClip CreateClipFromFloatArray(float[] samples, int sampleRate, string name = "clip")
    {
        if (samples == null || samples.Length == 0) return null;
        var clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}