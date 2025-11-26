using UnityEngine;
public class MaxineAudioProcessor : MonoBehaviour
{
    private AudioClip micClip;
    private float[] rawData;
    private float[] processedData;
    private const int sampleRate = 16000;
    private const int bufferSeconds = 5;

    void Start()
    {
        MaxineAudioFX.NvAFX_Initialize();
        micClip = Microphone.Start(null, false, bufferSeconds, sampleRate);
        int bufferSize = sampleRate * bufferSeconds;
        rawData = new float[bufferSize];
        processedData = new float[bufferSize];
    }

    void Update()
    {
        if (micClip == null || !Microphone.IsRecording(null))
            return;

        micClip.GetData(rawData, 0);
        MaxineAudioFX.NvAFX_ProcessAudio(rawData, processedData, rawData.Length);
    }

    void OnDestroy()
    {
        if (Microphone.IsRecording(null))
            Microphone.End(null);
        // Ideally: call a MaxineAudioFX cleanup method if available
    }
}
