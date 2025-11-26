using System.Runtime.InteropServices;
using UnityEngine;

public static class MaxineAudioFX
{
    [DllImport("nvafx.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_Initialize();

    [DllImport("nvafx.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int NvAFX_ProcessAudio(float[] input, float[] output, int length);

    public static bool Initialize()
    {
        int result = NvAFX_Initialize();
        if (result != 0)
            Debug.LogError($"NvAFX_Initialize failed with code {result}");
        return result == 0;
    }
}
