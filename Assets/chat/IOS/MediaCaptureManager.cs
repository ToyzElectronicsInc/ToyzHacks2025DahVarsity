using UnityEngine;

public class MediaCaptureManager : MonoBehaviour
{
    private WebCamTexture webcamTexture;
    private AudioClip micInput;


    void Start()
    {
        // Start webcam
        if (WebCamTexture.devices.Length > 0)
        {
            webcamTexture = new WebCamTexture();
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null) renderer.material.mainTexture = webcamTexture;
            webcamTexture.Play();
            
        }
        else
        {
            Debug.LogWarning("No webcam detected.");
        }
        
        if (Microphone.devices.Length > 0)
        {
            // Start microphone
            string micDevice = Microphone.devices[0];
            micInput = Microphone.Start(micDevice, true, 10, 44100);
        }
        else
        {
            Debug.LogWarning("No microphone detected.");
        }
    }

    public void StartCapture()
    {
        // Start webcam and mic if not already running
    }

    public void StopCapture()
    {
        webcamTexture?.Stop();
        Microphone.End(null);
    }


    public float[] GetMicData()
    {
        if (micInput == null) return null;
        int samples = micInput.samples * micInput.channels;
        float[] data = new float[samples];
        micInput.GetData(data, 0);
        return data;
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) webcamTexture?.Stop();
    }

    void OnApplicationQuit()
    {
        webcamTexture?.Stop();
        Microphone.End(null);
    }
}