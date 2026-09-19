using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using VoxAssist.Desktop.Models;

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
        GrokSttOptions options,
        Action<string, bool> onTranscriptReceived,
        CancellationToken ct)
    {
        var result = new SttResult { Format = "PCM" };
        long rawBytes = 0;
        long bytesSent = 0;

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        var wsUrl = options.BuildWebSocketUrl();

        Exception? sendEx = null;
        Exception? receiveEx = null;
        var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), ct);

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

                    if (audioBuffer.Count > 0)
                    {
                        var remaining = audioBuffer.ToArray();
                        rawBytes += remaining.Length;
                        bytesSent += remaining.Length;
                        await ws.SendAsync(new ArraySegment<byte>(remaining), WebSocketMessageType.Binary, true, ct);
                    }

                    var doneMsg = Encoding.UTF8.GetBytes("{\"type\":\"audio.done\"}");
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
                                            var doneText = ExtractTranscriptText(root, options.Diarize);
                                            if (!string.IsNullOrEmpty(doneText))
                                            {
                                                sttTextBuilder.Clear();
                                                sttTextBuilder.Append(doneText);
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

                                    var text = ExtractTranscriptText(root, options.Diarize);
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
                                            var currentAcc = sttTextBuilder.ToString();
                                            var merged = MergeOverlap(currentAcc, text);
                                            sttTextBuilder.Clear();
                                            sttTextBuilder.Append(merged);
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
            result.Duration = rawBytes / 32000.0;
        }
        catch (Exception ex)
        {
            result.Text = $"Error: {ex.Message}";
        }

        return result;
    }

    private static string? ExtractTranscriptText(JsonElement root, bool diarize)
    {
        if (diarize && root.TryGetProperty("words", out var words) && words.ValueKind == JsonValueKind.Array && words.GetArrayLength() > 0)
        {
            var formatted = FormatDiarizedWords(words);
            if (!string.IsNullOrEmpty(formatted))
                return formatted;
        }

        return root.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
    }

    private static string FormatDiarizedWords(JsonElement words)
    {
        var sb = new StringBuilder();
        int? lastSpeaker = null;

        foreach (var word in words.EnumerateArray())
        {
            if (!word.TryGetProperty("text", out var textProp)) continue;
            var wordText = textProp.GetString();
            if (string.IsNullOrEmpty(wordText)) continue;

            int? speaker = null;
            if (word.TryGetProperty("speaker", out var speakerProp) && speakerProp.TryGetInt32(out var speakerIndex))
            {
                speaker = speakerIndex;
            }

            if (speaker != lastSpeaker)
            {
                if (sb.Length > 0) sb.AppendLine();
                if (speaker.HasValue) sb.Append($"Speaker {speaker.Value + 1}: ");
                lastSpeaker = speaker;
            }
            else if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1]) && !char.IsPunctuation(wordText[0]))
            {
                sb.Append(' ');
            }

            sb.Append(wordText);
        }

        return sb.ToString();
    }

    private static string NormalizeForPrefixCompare(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    public static bool IsCumulativeUpdate(string accumulated, string incoming)
    {
        if (string.IsNullOrEmpty(accumulated) || string.IsNullOrEmpty(incoming)) return false;

        string normAcc = NormalizeForPrefixCompare(accumulated);
        string normInc = NormalizeForPrefixCompare(incoming);

        int compareLen = Math.Min(12, Math.Min(normAcc.Length, normInc.Length));
        if (compareLen == 0) return false;

        string accPrefix = normAcc.Substring(0, compareLen);
        string incPrefix = normInc.Substring(0, compareLen);

        return string.Equals(accPrefix, incPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string MergeOverlap(string accumulated, string incoming)
    {
        if (string.IsNullOrEmpty(accumulated)) return incoming;
        if (string.IsNullOrEmpty(incoming)) return accumulated;

        if (IsCumulativeUpdate(accumulated, incoming))
        {
            return incoming;
        }

        string accTrim = accumulated.Trim();
        string incTrim = incoming.Trim();

        int maxOverlap = Math.Min(accTrim.Length, incTrim.Length);
        int overlapLength = 0;

        for (int len = maxOverlap; len > 0; len--)
        {
            string suffix = accTrim.Substring(accTrim.Length - len);
            string prefix = incTrim.Substring(0, len);

            if (string.Equals(suffix, prefix, StringComparison.OrdinalIgnoreCase))
            {
                bool leftBoundary = (accTrim.Length - len - 1 < 0) ||
                                     char.IsWhiteSpace(accTrim[accTrim.Length - len - 1]) ||
                                     char.IsPunctuation(accTrim[accTrim.Length - len - 1]);

                bool rightBoundary = (len >= incTrim.Length) ||
                                      char.IsWhiteSpace(incTrim[len]) ||
                                      char.IsPunctuation(incTrim[len]);

                if (leftBoundary && rightBoundary)
                {
                    overlapLength = len;
                    break;
                }
            }
        }

        if (overlapLength > 0)
        {
            string newSuffix = incTrim.Substring(overlapLength);
            if (string.IsNullOrWhiteSpace(newSuffix))
            {
                return accumulated;
            }

            bool needsSpace = !accumulated.EndsWith(" ") && !newSuffix.StartsWith(" ") && !char.IsPunctuation(newSuffix[0]);
            return accumulated + (needsSpace ? " " : "") + newSuffix;
        }

        bool needsDefaultSpace = !accumulated.EndsWith(" ") && !incoming.StartsWith(" ") && !char.IsPunctuation(incoming[0]);
        return accumulated + (needsDefaultSpace ? " " : "") + incoming;
    }
}

public class ChatMessage
{
    public string role { get; set; } = "";
    public string content { get; set; } = "";
}
