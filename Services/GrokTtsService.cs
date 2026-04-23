using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VoxAssist.Desktop.Services;

public class GrokTtsService
{
    private readonly HttpClient _httpClient = new();

    public async Task<byte[]?> GenerateSpeechAsync(string text, string apiKey, string voiceId = "eve")
    {
        try
        {
            var requestBody = new
            {
                text = text,
                voice_id = voiceId,
                language = "en",
                output_format = new
                {
                    codec = "mp3",
                    sample_rate = 24000
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/tts");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Grok TTS API Error: {response.StatusCode} - {error}");
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GrokTtsService Error: {ex.Message}");
            return null;
        }
    }
}
