using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VoxAssist.Desktop.Models;
using ManagedBass;
using ManagedBass.Enc;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading;
using System.Net;

namespace VoxAssist.Desktop.Services;

public class GrokResponse
{
    public string Keyboard { get; set; } = "";
    public string Markdown { get; set; } = "";
}

public class GrokService
{
    private readonly HttpClient _httpClient;

    public GrokService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<string> StreamSpeechToTextAsync(ChannelReader<byte[]> pcmReader, string apiKey, string language, CompressionType compression, CancellationToken ct)
    {
        try
        {
            if (!Bass.Init(0)) 
            {
                if (Bass.LastError != Errors.Already)
                    return $"Error: BASS Init failed: {Bass.LastError}";
            }

            var boundary = "----VoxAssistBoundary" + Guid.NewGuid().ToString("N");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/stt");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var streamingContent = new PushStreamContent(async (outputStream, content, context) =>
            {
                using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), 1024, true);

                await WriteFormField(writer, boundary, "model", "grok-2-audio-preview");
                await WriteFormField(writer, boundary, "language", language);
                await WriteFormField(writer, boundary, "format", "true");

                await writer.WriteAsync($"--{boundary}\r\n");
                await writer.WriteAsync($"Content-Disposition: form-data; name=\"file\"; filename=\"audio.{(compression == CompressionType.Flac ? "flac" : "wav")}\"\r\n");
                await writer.WriteAsync($"Content-Type: {(compression == CompressionType.Flac ? "audio/flac" : "audio/wav")}\r\n\r\n");
                await writer.FlushAsync();

                if (compression == CompressionType.Flac)
                {
                    await StreamEncodeToFlacAsync(pcmReader, outputStream, ct);
                }
                else
                {
                    await StreamRawToWavAsync(pcmReader, outputStream, ct);
                }

                await writer.WriteAsync($"\r\n--{boundary}--\r\n");
                await writer.FlushAsync();
            });

            streamingContent.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
            request.Content = streamingContent;

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            var resultJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return $"Error: {response.StatusCode} - {resultJson}";
            }

            using var doc = JsonDocument.Parse(resultJson);
            return doc.RootElement.GetProperty("text").GetString() ?? "";
        }
        catch (OperationCanceledException) { return "Cancelled"; }
        catch (Exception ex)
        {
            return $"Error in streaming STT: {ex.Message}";
        }
    }

    private async Task WriteFormField(StreamWriter writer, string boundary, string name, string value)
    {
        await writer.WriteAsync($"--{boundary}\r\n");
        await writer.WriteAsync($"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
        await writer.WriteAsync($"{value}\r\n");
    }

    private async Task StreamEncodeToFlacAsync(ChannelReader<byte[]> reader, Stream outputStream, CancellationToken ct)
    {
        int pushStream = Bass.CreateStream(16000, 1, BassFlags.Decode, StreamProcedureType.Push);
        if (pushStream == 0) throw new Exception($"BASS CreateStream error: {Bass.LastError}");

        try
        {
            var encodeCallback = new EncodeProcedureEx((handle, channel, buffer, len, offset, user) => 
            {
                byte[] data = new byte[len];
                Marshal.Copy(buffer, data, 0, len);
                outputStream.Write(data, 0, len);
                outputStream.Flush();
            });

            var flacEncoder = BassEnc_Flac.Start(pushStream, "-8 -", EncodeFlags.NoHeader, encodeCallback, IntPtr.Zero);
            if (flacEncoder == 0) throw new Exception($"BASSenc_Flac Start error: {Bass.LastError}");

            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var chunk))
                {
                    Bass.StreamPutData(pushStream, chunk, chunk.Length);
                    byte[] dummy = new byte[chunk.Length];
                    Bass.ChannelGetData(pushStream, dummy, dummy.Length);
                }
            }

            Bass.StreamPutData(pushStream, IntPtr.Zero, 0);
            byte[] finalDummy = new byte[4096];
            while (Bass.ChannelGetData(pushStream, finalDummy, finalDummy.Length) > 0) { }

            BassEnc.EncodeStop(flacEncoder);
        }
        finally
        {
            Bass.StreamFree(pushStream);
        }
    }

    private async Task StreamRawToWavAsync(ChannelReader<byte[]> reader, Stream outputStream, CancellationToken ct)
    {
        var ms = new MemoryStream();
        AddWavHeaderToStream(ms, 0x7FFFFFFF); 
        outputStream.Write(ms.ToArray(), 0, 44);

        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var chunk))
            {
                await outputStream.WriteAsync(chunk, 0, chunk.Length, ct);
                await outputStream.FlushAsync(ct);
            }
        }
    }

    public async Task<string> SpeechToTextAsync(Stream rawAudioStream, long length, string apiKey, string language, CompressionType compression)
    {
        try
        {
            Stream audioStreamToUpload;
            string fileName;
            string contentType;

            if (!Bass.Init(0)) 
            {
                if (Bass.LastError != Errors.Already) return $"Error: BASS Init failed: {Bass.LastError}";
            }

            if (compression == CompressionType.Flac)
            {
                audioStreamToUpload = await NativeConvertToFlacAsync(rawAudioStream, (int)length);
                fileName = "audio.flac";
                contentType = "audio/flac";
            }
            else
            {
                var ms = new MemoryStream();
                AddWavHeaderToStream(ms, (int)length);
                rawAudioStream.Position = 0;
                await rawAudioStream.CopyToAsync(ms);
                ms.Position = 0;
                audioStreamToUpload = ms;
                fileName = "audio.wav";
                contentType = "audio/wav";
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/stt");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var content = new MultipartFormDataContent();
            content.Add(new StringContent("grok-2-audio-preview"), "model");
            content.Add(new StringContent(language), "language");
            content.Add(new StringContent("true"), "format"); 
            var audioContent = new StreamContent(audioStreamToUpload);
            audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            content.Add(audioContent, "file", fileName); 
            request.Content = content;
            var response = await _httpClient.SendAsync(request);
            var resultJson = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return $"Error: {response.StatusCode} - {resultJson}";
            using var doc = JsonDocument.Parse(resultJson);
            return doc.RootElement.GetProperty("text").GetString() ?? "";
        }
        catch (Exception ex) { return $"Error in STT: {ex.Message}"; }
    }

    private async Task<Stream> NativeConvertToFlacAsync(Stream rawAudioStream, int length)
    {
        var outStream = new MemoryStream();
        byte[] pcmData = new byte[length];
        rawAudioStream.Position = 0;
        int read = 0;
        while (read < length)
        {
            int r = await rawAudioStream.ReadAsync(pcmData, read, length - read);
            if (r == 0) break;
            read += r;
        }

        int pushStream = Bass.CreateStream(16000, 1, BassFlags.Decode, StreamProcedureType.Push);
        try {
            var encodeCallback = new EncodeProcedureEx((handle, channel, buffer, len, offset, user) => {
                byte[] data = new byte[len];
                Marshal.Copy(buffer, data, 0, len);
                outStream.Write(data, 0, len);
            });
            var flacEncoder = BassEnc_Flac.Start(pushStream, "-8 -", EncodeFlags.NoHeader, encodeCallback, IntPtr.Zero);
            Bass.StreamPutData(pushStream, pcmData, length);
            Bass.StreamPutData(pushStream, IntPtr.Zero, 0);
            byte[] dummyBuffer = new byte[4096];
            while (Bass.ChannelGetData(pushStream, dummyBuffer, dummyBuffer.Length) > 0) { }
            BassEnc.EncodeStop(flacEncoder);
        } finally { Bass.StreamFree(pushStream); }
        outStream.Position = 0;
        return outStream;
    }

    private void AddWavHeaderToStream(Stream stream, int rawDataLength)
    {
        var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + rawDataLength);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(16000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data".ToCharArray());
        writer.Write(rawDataLength);
    }

    public async Task<GrokResponse?> ProcessActionAsync(string text, string prompt, string apiKey, string baseUrl, string model)
    {
        try
        {
            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = prompt },
                    new { role = "user", content = text }
                },
                response_format = new { type = "json_object" }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var resultJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resultJson);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            
            if (string.IsNullOrEmpty(content)) return null;

            return JsonSerializer.Deserialize<GrokResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }
}

public class PushStreamContent : HttpContent
{
    private readonly Func<Stream, HttpContent, TransportContext?, Task> _onStreamAvailable;
    public PushStreamContent(Func<Stream, HttpContent, TransportContext?, Task> onStreamAvailable) { _onStreamAvailable = onStreamAvailable; }
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) { await _onStreamAvailable(stream, this, context); }
    protected override bool TryComputeLength(out long length) { length = -1; return false; }
}
