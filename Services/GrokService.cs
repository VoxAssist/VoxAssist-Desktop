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
using System.Net.WebSockets;

namespace VoxAssist.Desktop.Services;

public class GrokResponse
{
    public string? Keyboard { get; set; }
    public string? Markdown { get; set; }
    public string? Error { get; set; }
    public string? LlmRequest { get; set; }
    public string? FullResponse { get; set; }
}

public class SttResult
{
    public string Text { get; set; } = "";
    public double Duration { get; set; }
    public string Format { get; set; } = "PCM";
    public long RawBytes { get; set; }
    public long BytesSent { get; set; }
}

public class GrokService
{
    private readonly HttpClient _httpClient;

    public GrokService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<SttResult> StreamSpeechToTextAsync(ChannelReader<byte[]> pcmReader, string apiKey, string language, CompressionType compression, CancellationToken ct)
    {
        var result = new SttResult { Format = compression == CompressionType.None ? "PCM" : compression.ToString() };
        long rawBytes = 0;
        long bytesSentTotal = 0;

        try
        {
            if (!Bass.Init(0)) 
            {
                if (Bass.LastError != Errors.Already)
                    return new SttResult { Text = $"Error: BASS Init failed: {Bass.LastError}" };
            }

            var boundary = "----VoxAssistBoundary" + Guid.NewGuid().ToString("N");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.ai/v1/stt");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var streamingContent = new PushStreamContent(async (outputStream, content, context) =>
            {
                using var writer = new StreamWriter(outputStream, new UTF8Encoding(false), 1024, true);

                await WriteFormField(writer, boundary, "language", language);
                await WriteFormField(writer, boundary, "format", "true");

                string fileName = compression == CompressionType.Flac ? "audio.flac" : "audio.wav";
                string contentType = compression == CompressionType.Flac ? "audio/flac" : "audio/wav";

                await writer.WriteAsync($"--{boundary}\r\n");
                await writer.WriteAsync($"Content-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\n");
                await writer.WriteAsync($"Content-Type: {contentType}\r\n\r\n");
                await writer.FlushAsync();

                if (compression == CompressionType.Flac)
                {
                    var res = await StreamEncodeToFlacAsync(pcmReader, outputStream, ct);
                    rawBytes = res.raw;
                    bytesSentTotal = res.sent;
                }
                else if (compression == CompressionType.G711)
                {
                    var res = await StreamEncodeToG711Async(pcmReader, outputStream, ct);
                    rawBytes = res.raw;
                    bytesSentTotal = res.sent;
                }
                else
                {
                    var res = await StreamRawToWavAsync(pcmReader, outputStream, ct);
                    rawBytes = res.raw;
                    bytesSentTotal = res.sent;
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
                result.Text = $"Error: {response.StatusCode} - {resultJson}";
                return result;
            }

            using var doc = JsonDocument.Parse(resultJson);
            result.Text = doc.RootElement.GetProperty("text").GetString() ?? "";
            result.RawBytes = rawBytes;
            result.BytesSent = bytesSentTotal;
            result.Duration = rawBytes / 32000.0;
            
            return result;
        }
        catch (OperationCanceledException) { result.Text = "Cancelled"; return result; }
        catch (Exception ex)
        {
            result.Text = $"Error in streaming STT: {ex.Message}";
            return result;
        }
    }

    private async Task WriteFormField(StreamWriter writer, string boundary, string name, string value)
    {
        await writer.WriteAsync($"--{boundary}\r\n");
        await writer.WriteAsync($"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
        await writer.WriteAsync($"{value}\r\n");
    }

    private async Task<(long raw, long sent)> StreamEncodeToG711Async(ChannelReader<byte[]> reader, Stream outputStream, CancellationToken ct)
    {
        long pcmBytesProcessed = 0;
        long bytesSent = 0;

        var ms = new MemoryStream();
        AddWavHeaderToStream(ms, 100 * 1024 * 1024, isG711: true); 
        var header = ms.ToArray();
        await outputStream.WriteAsync(header, 0, header.Length, ct);
        bytesSent += header.Length;

        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var chunk))
            {
                pcmBytesProcessed += chunk.Length;
                byte[] encoded = new byte[chunk.Length / 2];
                for (int i = 0; i < encoded.Length; i++)
                {
                    short sample = BitConverter.ToInt16(chunk, i * 2);
                    encoded[i] = LinearToMuLaw(sample);
                }
                await outputStream.WriteAsync(encoded, 0, encoded.Length, ct);
                bytesSent += encoded.Length;
            }
        }
        return (pcmBytesProcessed, bytesSent);
    }

    private static byte LinearToMuLaw(short sample)
    {
        const int cBias = 0x84;
        const int cClip = 32635;
        int sign = (sample >> 8) & 0x80;
        if (sign != 0) sample = (short)-sample;
        if (sample > cClip) sample = (short)cClip;
        sample += cBias;
        int exponent = 7;
        for (int expMask = 0x4000; (sample & expMask) == 0 && exponent > 0; exponent--, expMask >>= 1) { }
        int mantissa = (sample >> (exponent + 3)) & 0x0F;
        return (byte)(~(sign | (exponent << 4) | mantissa));
    }

    private async Task<(long raw, long sent)> StreamEncodeToFlacAsync(ChannelReader<byte[]> reader, Stream outputStream, CancellationToken ct)
    {
        long pcmBytesProcessed = 0;
        long bytesSent = 0;
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
                bytesSent += len;
            });

            var flacEncoder = BassEnc_Flac.Start(pushStream, "-8 -", EncodeFlags.NoHeader, encodeCallback, IntPtr.Zero);
            if (flacEncoder == 0) throw new Exception($"BASSenc_Flac Start error: {Bass.LastError}");

            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var chunk))
                {
                    pcmBytesProcessed += chunk.Length;
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
        finally { Bass.StreamFree(pushStream); }
        return (pcmBytesProcessed, bytesSent);
    }

    private async Task<(long raw, long sent)> StreamRawToWavAsync(ChannelReader<byte[]> reader, Stream outputStream, CancellationToken ct)
    {
        long pcmBytesProcessed = 0;
        long bytesSent = 0;
        var ms = new MemoryStream();
        AddWavHeaderToStream(ms, 100 * 1024 * 1024, isG711: false); 
        var header = ms.ToArray();
        outputStream.Write(header, 0, 44);
        bytesSent += 44;

        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var chunk))
            {
                pcmBytesProcessed += chunk.Length;
                await outputStream.WriteAsync(chunk, 0, chunk.Length, ct);
                await outputStream.FlushAsync(ct);
                bytesSent += chunk.Length;
            }
        }
        return (pcmBytesProcessed, bytesSent);
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
                AddWavHeaderToStream(ms, (int)length, compression == CompressionType.G711);
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

    private void AddWavHeaderToStream(Stream stream, int rawDataLength, bool isG711 = false)
    {
        var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("RIFF".ToCharArray());
        writer.Write((isG711 ? 50 : 36) + rawDataLength);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(isG711 ? 18 : 16);
        writer.Write((short)(isG711 ? 7 : 1));
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(isG711 ? 16000 : 32000);
        writer.Write((short)(isG711 ? 1 : 2));
        writer.Write((short)(isG711 ? 8 : 16));
        if (isG711) writer.Write((short)0);
        if (isG711)
        {
            writer.Write("fact".ToCharArray());
            writer.Write(4);
            writer.Write(rawDataLength);
        }
        writer.Write("data".ToCharArray());
        writer.Write(rawDataLength);
    }

    public async Task<GrokResponse?> ProcessActionAsync(List<ChatMessage> messages, string apiKey, string baseUrl, string model)
    {
        string requestJson = "";
        try
        {
            var sanitizedBaseUrl = baseUrl.TrimEnd('/');
            if (sanitizedBaseUrl.EndsWith("/stt")) sanitizedBaseUrl = sanitizedBaseUrl.Substring(0, sanitizedBaseUrl.Length - 4);

            var requestBody = new { model = model, messages = messages, response_format = new { type = "json_object" } };
            requestJson = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
            var fullUrl = $"{sanitizedBaseUrl.TrimEnd('/')}/chat/completions";
            
            using var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new GrokResponse { Error = $"LLM API Error (at {fullUrl}): {response.StatusCode} - {error}\nRequest Body: {requestJson}" };
            }

            var resultJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resultJson);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrEmpty(content)) return new GrokResponse { Error = "LLM returned empty content.", FullResponse = resultJson };

            var cleanedContent = content.Trim();
            if (cleanedContent.StartsWith("```"))
            {
                int firstNewline = cleanedContent.IndexOf('\n');
                int lastBacktick = cleanedContent.LastIndexOf("```");
                if (firstNewline != -1 && lastBacktick > firstNewline) cleanedContent = cleanedContent.Substring(firstNewline, lastBacktick - firstNewline).Trim();
            }

            try 
            {
                var grokResult = JsonSerializer.Deserialize<GrokResponse>(cleanedContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (grokResult != null) 
                {
                    grokResult.LlmRequest = requestJson;
                    try { using var jsonDoc = JsonDocument.Parse(resultJson); grokResult.FullResponse = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true }); }
                    catch { grokResult.FullResponse = resultJson; }
                }
                return grokResult;
            }
            catch (Exception ex)
            {
                var fallbackResponse = resultJson;
                try { using var jsonDoc = JsonDocument.Parse(resultJson); fallbackResponse = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true }); } catch { }
                return new GrokResponse { Error = $"JSON Parse Error: {ex.Message}. Raw content: {content}", LlmRequest = requestJson, FullResponse = fallbackResponse };
            }
        }
        catch (Exception ex) { return new GrokResponse { Error = $"ProcessActionAsync Exception: {ex.Message}" }; }
    }

    public async Task<SttResult> StreamSpeechToTextWebsocketAsync(
        ChannelReader<byte[]> pcmReader, 
        string apiKey, 
        string language, 
        Action<string, bool> onTranscriptReceived, 
        CancellationToken ct)
    {
        var result = new SttResult { Format = "PCM" };
        long rawBytes = 0;
        long bytesSent = 0;

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        // Construct WebSocket URL
        // sample_rate=16000, encoding=pcm, language, interim_results=true
        var wsUrl = $"wss://api.x.ai/v1/stt?sample_rate=16000&encoding=pcm&language={Uri.EscapeDataString(language)}&interim_results=true";

        Exception? sendEx = null;
        Exception? receiveEx = null;
        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), ct);

            // Send task and Receive task
            var sendTask = Task.Run(async () =>
            {
                try
                {
                    var isReady = await readyTcs.Task;
                    if (!isReady)
                    {
                        return;
                    }

                    var audioBuffer = new List<byte>();

                    while (await pcmReader.WaitToReadAsync(ct))
                    {
                        while (pcmReader.TryRead(out var chunk))
                        {
                            if (chunk.Length > 0)
                            {
                                audioBuffer.AddRange(chunk);

                                // 3200 bytes = 100 ms for 16kHz 16-bit Mono PCM
                                while (audioBuffer.Count >= 3200)
                                {
                                    var chunkToSend = new byte[3200];
                                    audioBuffer.CopyTo(0, chunkToSend, 0, 3200);
                                    audioBuffer.RemoveRange(0, 3200);

                                    rawBytes += chunkToSend.Length;
                                    bytesSent += chunkToSend.Length;
                                    await ws.SendAsync(new ArraySegment<byte>(chunkToSend), WebSocketMessageType.Binary, true, ct);
                                }
                            }
                        }
                    }

                    // Send any remaining bytes
                    if (audioBuffer.Count > 0)
                    {
                        var remaining = audioBuffer.ToArray();
                        rawBytes += remaining.Length;
                        bytesSent += remaining.Length;
                        await ws.SendAsync(new ArraySegment<byte>(remaining), WebSocketMessageType.Binary, true, ct);
                    }

                    // Send audio.done text control signal to xAI STT
                    var doneMsg = Encoding.UTF8.GetBytes("{\"type\": \"audio.done\"}");
                    await ws.SendAsync(new ArraySegment<byte>(doneMsg), WebSocketMessageType.Text, true, ct);
                }
                catch (Exception ex)
                {
                    sendEx = ex;
                    readyTcs.TrySetResult(false);
                }
            }, ct);

            var sttTextBuilder = new StringBuilder();

            var receiveTask = Task.Run(async () =>
            {
                var buffer = new byte[8192];
                var messageBuilder = new StringBuilder();

                try
                {
                    while (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseSent || ws.State == WebSocketState.CloseReceived)
                    {
                        var wsResult = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (wsResult.MessageType == WebSocketMessageType.Close)
                        {
                            try
                            {
                                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Acknowledge Close", ct);
                            }
                            catch { }
                            break;
                        }

                        if (wsResult.MessageType == WebSocketMessageType.Text)
                        {
                            var textChunk = Encoding.UTF8.GetString(buffer, 0, wsResult.Count);
                            messageBuilder.Append(textChunk);

                            if (wsResult.EndOfMessage)
                            {
                                var jsonStr = messageBuilder.ToString();
                                messageBuilder.Clear();
                                Console.Error.WriteLine($"[WS RECEIVE] {jsonStr}");

                                try
                                {
                                    using var doc = JsonDocument.Parse(jsonStr);
                                    var root = doc.RootElement;

                                    if (root.TryGetProperty("type", out var typeProp))
                                    {
                                        var typeStr = typeProp.GetString();
                                        if (typeStr == "transcript.created")
                                        {
                                            readyTcs.TrySetResult(true);
                                            continue;
                                        }
                                        else if (typeStr == "transcript.done")
                                        {
                                            if (root.TryGetProperty("text", out var doneTextProp))
                                            {
                                                var doneText = doneTextProp.GetString();
                                                if (!string.IsNullOrEmpty(doneText))
                                                {
                                                    sttTextBuilder.Clear();
                                                    sttTextBuilder.Append(doneText);
                                                }
                                            }

                                            try
                                            {
                                                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", ct);
                                            }
                                            catch { }
                                            break;
                                        }
                                        else if (typeStr == "error")
                                        {
                                            string errMsg = "Unknown WebSocket error";
                                            if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
                                            {
                                                if (errorProp.TryGetProperty("message", out var msgProp))
                                                {
                                                    errMsg = msgProp.GetString() ?? errMsg;
                                                }
                                            }
                                            else if (root.TryGetProperty("message", out var msgPropDirect))
                                            {
                                                errMsg = msgPropDirect.GetString() ?? errMsg;
                                            }

                                            receiveEx = new Exception(errMsg);
                                            readyTcs.TrySetResult(false);
                                            break;
                                        }
                                    }

                                    if (root.TryGetProperty("text", out var textProp))
                                    {
                                        var text = textProp.GetString();
                                        bool isFinal = false;
                                        if (root.TryGetProperty("is_final", out var finalProp))
                                        {
                                            isFinal = finalProp.GetBoolean();
                                        }

                                        if (!string.IsNullOrEmpty(text))
                                        {
                                            onTranscriptReceived(text, isFinal);
                                            
                                            if (isFinal)
                                            {
                                                if (sttTextBuilder.Length > 0 && !sttTextBuilder.ToString().EndsWith(" ") && !text.StartsWith(" ") && !char.IsPunctuation(text[0]))
                                                {
                                                    sttTextBuilder.Append(" ");
                                                }
                                                sttTextBuilder.Append(text);
                                            }
                                        }
                                    }
                                }
                                catch (JsonException)
                                {
                                    // Ignore JSON parse errors
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    receiveEx = ex;
                    readyTcs.TrySetResult(false);
                }
                finally
                {
                    readyTcs.TrySetResult(false);
                }
            }, ct);

            // Wait for both tasks to finish
            await Task.WhenAll(sendTask, receiveTask);

            if (sttTextBuilder.Length == 0)
            {
                if (receiveEx != null)
                {
                    result.Text = $"Error: Receive failed: {receiveEx.Message}";
                }
                else if (sendEx != null)
                {
                    result.Text = $"Error: Send failed: {sendEx.Message}";
                }
                else
                {
                    result.Text = "Error: Connection closed by server or empty transcript returned.";
                }
            }
            else
            {
                result.Text = sttTextBuilder.ToString();
            }

            result.RawBytes = rawBytes;
            result.BytesSent = bytesSent;
            result.Duration = rawBytes / 32000.0; // 16kHz 16-bit mono = 32000 bytes/sec
        }
        catch (Exception ex)
        {
            result.Text = $"Error: {ex.Message}";
        }

        return result;
    }
}

public class ChatMessage
{
    public string role { get; set; } = "";
    public string content { get; set; } = "";
}

public class PushStreamContent : HttpContent
{
    private readonly Func<Stream, HttpContent, TransportContext?, Task> _onStreamAvailable;
    public PushStreamContent(Func<Stream, HttpContent, TransportContext?, Task> onStreamAvailable) { _onStreamAvailable = onStreamAvailable; }
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) { await _onStreamAvailable(stream, this, context); }
    protected override bool TryComputeLength(out long length) { length = -1; return false; }
}
