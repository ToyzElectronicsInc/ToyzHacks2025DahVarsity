// WavUtils.cs (place in Utilities/ or Shared/)
using System.IO;
using UnityEngine;

public static class WavUtils
{
    // Convert PCM float samples [-1..1] (mono) to 16-bit PCM WAV bytes with given sampleRate.
    /*public static byte[] FloatArrayToWav16(float[] samples, int sampleRate = 16000)
    {
        if (samples == null || samples.Length == 0) return new byte[0];
        int byteCount = samples.Length * 2; // 16-bit
        using var ms = new MemoryStream(44 + byteCount);
        using (var bw = new BinaryWriter(ms))
        {
            // RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + byteCount);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // subchunk1 size
            bw.Write((short)1); // PCM
            bw.Write((short)1); // mono
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // byte rate
            bw.Write((short)2); // block align
            bw.Write((short)16); // bits/sample

            // data chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            bw.Write(byteCount);

            // write pcm16
            foreach (var f in samples)
            {
                float clamped = Mathf.Clamp(f, -1f, 1f);
                short s = (short)Mathf.RoundToInt(clamped * 32767f);
                bw.Write(s);
            }
        }
        return ms.ToArray();
    }

    // Helper to convert multi-channel float[] into mono float[] by averaging channels.
    public static float[] ToMono(float[] src, int channels)
    {
        if (channels <= 1) return src;
        int frames = src.Length / channels;
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float s = 0f;
            for (int c = 0; c < channels; c++) s += src[f * channels + c];
            mono[f] = s / channels;
        }
        return mono;
    }*/

    public static byte[] AudioClipToWav16(AudioClip clip, int targetSampleRate = 16000)
    {
        if (clip == null) return new byte[0];

        int channels = clip.channels;
        int srcRate = clip.frequency;
        float[] srcData = new float[clip.samples * channels];
        clip.GetData(srcData, 0);

        float[] mono = ToMono(srcData, channels);
        float[] resampled = (srcRate == targetSampleRate) ? mono : ResampleLinear(mono, srcRate, targetSampleRate);

        return FloatArrayToWav16(resampled, targetSampleRate);
    }

    // Simple linear resampler (keeps code small / deterministic)
    public static float[] ResampleLinear(float[] inSamples, int inRate, int outRate)
    {
        if (inSamples == null || inSamples.Length == 0) return new float[0];
        if (inRate == outRate) return inSamples;
        double ratio = (double)inRate / outRate;
        int outLen = Mathf.Max(1, (int)(inSamples.Length / ratio));
        var outSamples = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            double srcPos = i * ratio;
            int iPos = Mathf.FloorToInt((float)srcPos);
            double frac = srcPos - iPos;
            float s0 = (iPos >= 0 && iPos < inSamples.Length) ? inSamples[iPos] : 0f;
            float s1 = (iPos + 1 >= 0 && iPos + 1 < inSamples.Length) ? inSamples[iPos + 1] : 0f;
            outSamples[i] = (float)((1.0 - frac) * s0 + frac * s1);
        }
        return outSamples;
    }

    // Convert PCM16 bytes (little-endian) -> float[] [-1..1]
    public static float[] Pcm16LeToFloatArray(byte[] pcmLe)
    {
        if (pcmLe == null || pcmLe.Length == 0) return new float[0];
        int samples = pcmLe.Length / 2;
        var f = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            short v = (short)(pcmLe[2 * i] | (pcmLe[2 * i + 1] << 8));
            f[i] = v / 32768f;
        }
        return f;
    }
}