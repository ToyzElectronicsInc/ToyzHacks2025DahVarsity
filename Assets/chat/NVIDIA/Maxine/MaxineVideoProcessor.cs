using UnityEngine;
using UnityEngine.UI;

public class MaxineVideoProcessor : MonoBehaviour
{
    public RawImage display;
    private WebCamTexture webcam;

    void Start()
    {
        webcam = new WebCamTexture();
        display.texture = webcam;
        webcam.Play();
    }

    void Update()
    {
        if (webcam == null || !webcam.isPlaying) return;

        // Convert webcam frame to byte array for Maxine processing
        Color32[] pixels = webcam.GetPixels32();
        byte[] frameBytes = new byte[pixels.Length * 4];
        System.Buffer.BlockCopy(pixels, 0, frameBytes, 0, frameBytes.Length);

        // TODO: Send frameBytes to native Maxine video SDK for eye contact / background removal

        // Update display with processed frame if available
    }
    void OnDestroy()
    {
        if (webcam != null && webcam.isPlaying)
            webcam.Stop();
    }
}
