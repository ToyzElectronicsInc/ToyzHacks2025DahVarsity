using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

public class RivaTranscriptionClient
{
    private readonly HttpClient client = new HttpClient();
    private const string endpoint = "http://localhost:5000/transcribe"; // proxy to Riva gRPC

    public async Task<string> TranscribeAsync(byte[] audioData)
    {
        var content = new ByteArrayContent(audioData);
        content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        var response = await client.PostAsync(endpoint, content);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Riva proxy error: {response.StatusCode}");
        return await response.Content.ReadAsStringAsync();
    }
}
