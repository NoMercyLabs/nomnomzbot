// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Free TTS provider using the Microsoft Edge Read Aloud WebSocket API.
/// No API key required — works immediately for all channels.
///
/// Uses the unofficial wss://speech.platform.bing.com endpoint that powers
/// Edge's built-in read-aloud feature.
/// </summary>
public sealed class EdgeTtsProvider : ITtsProvider
{
    private const string ProviderName = "edge";
    private const string TrustedToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    /// <summary>Same read-aloud surface the synthesis WebSocket uses, so the token stays a single constant.</summary>
    private const string VoiceListUrl =
        $"https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list?trustedclienttoken={TrustedToken}";

    internal const string HttpClientName = "edge-tts";

    private readonly TimeProvider _timeProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EdgeTtsProvider> _logger;

    public EdgeTtsProvider(
        TimeProvider timeProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<EdgeTtsProvider> logger
    )
    {
        _timeProvider = timeProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<TtsSynthesisResult> SynthesizeAsync(
        string text,
        string voiceId,
        CancellationToken cancellationToken = default
    )
    {
        string connectionId = Guid.NewGuid().ToString("N");
        string url =
            $"wss://speech.platform.bing.com/consumer/speech/synthesize/realtimetts/edge/v1?TrustedClientToken={TrustedToken}&ConnectionId={connectionId}";

        using ClientWebSocket ws = new();
        ws.Options.SetRequestHeader(
            "Origin",
            "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold"
        );
        ws.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
        );

        try
        {
            await ws.ConnectAsync(new(url), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Edge TTS: Failed to connect");
            return EmptyResult(voiceId);
        }

        // Send configuration
        string configMsg = BuildConfigMessage(connectionId);
        await SendTextAsync(ws, configMsg, cancellationToken);

        // Send synthesis request
        string requestId = Guid.NewGuid().ToString("N");
        string ssml = BuildSsml(text, voiceId);
        string synthesisMsg = BuildSynthesisMessage(requestId, ssml);
        await SendTextAsync(ws, synthesisMsg, cancellationToken);

        // Collect audio chunks
        using MemoryStream audioStream = new();
        byte[] buffer = new byte[32768];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                using MemoryStream msgStream = new();
                WebSocketReceiveResult result;

                do
                {
                    result = await ws.ReceiveAsync(buffer, cancellationToken);
                    msgStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                byte[] msgBytes = msgStream.ToArray();

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Binary message: header\r\n\r\n<audio bytes>
                    int headerEnd = FindHeaderEnd(msgBytes);
                    if (headerEnd >= 0)
                    {
                        int audioStart = headerEnd + 4;
                        if (audioStart < msgBytes.Length)
                            audioStream.Write(msgBytes, audioStart, msgBytes.Length - audioStart);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    string text2 = Encoding.UTF8.GetString(msgBytes);
                    if (text2.Contains("turn.end"))
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Edge TTS: Error receiving audio for voice {VoiceId}", voiceId);
        }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Done",
                        CancellationToken.None
                    );
                }
                catch
                { /* ignore close errors */
                }
            }
        }

        byte[] audioData = audioStream.ToArray();
        if (audioData.Length == 0)
        {
            _logger.LogWarning("Edge TTS: No audio received for voice {VoiceId}", voiceId);
            return EmptyResult(voiceId);
        }

        // Estimate duration: MP3 ~128kbps = 16 KB/s
        int durationMs = (int)(audioData.Length / 16.0 * 1000.0 / 1024.0);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text + voiceId)))[
            ..16
        ];

        return new()
        {
            AudioData = audioData,
            DurationMs = durationMs,
            Provider = ProviderName,
            VoiceId = voiceId,
            ContentHash = hash,
        };
    }

    /// <summary>
    /// The full live Edge read-aloud voice list (300+ voices — full external-API coverage). Any fetch or
    /// parse failure falls back to the curated subset, so the catalogue sync (upsert-only) still refreshes
    /// the well-known voices and never loses rows to a transient outage.
    /// </summary>
    public async Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
            using HttpRequestMessage request = new(HttpMethod.Get, VoiceListUrl);
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
            );
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            IReadOnlyList<TtsVoiceInfo> voices = ParseVoiceList(json);
            if (voices.Count == 0)
            {
                _logger.LogWarning(
                    "Edge TTS: live voice list came back empty; using the curated fallback."
                );
                return FallbackVoices;
            }
            return voices;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Edge TTS: failed to fetch the live voice list; using the curated fallback."
            );
            return FallbackVoices;
        }
    }

    /// <summary>
    /// Maps the read-aloud list payload (Name/ShortName/Gender/Locale/FriendlyName per voice) to
    /// <see cref="TtsVoiceInfo"/>: ShortName is the synthesizable voice id, the given name is derived from it
    /// ("en-US-AnaNeural" → "Ana"), and the display name follows the curated "Ana (US)" style. Rows missing a
    /// required field are skipped rather than failing the whole list.
    /// </summary>
    internal static IReadOnlyList<TtsVoiceInfo> ParseVoiceList(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        List<TtsVoiceInfo> voices = [];
        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string? shortName = GetString(element, "ShortName");
            string? locale = GetString(element, "Locale");
            string? gender = GetString(element, "Gender");
            if (shortName is null || locale is null || gender is null)
                continue;

            string givenName = DeriveGivenName(shortName, locale);
            string region = locale[(locale.LastIndexOf('-') + 1)..];
            voices.Add(
                new TtsVoiceInfo
                {
                    Id = shortName,
                    Name = givenName,
                    DisplayName = $"{givenName} ({region})",
                    Locale = locale,
                    Gender = gender,
                    Provider = ProviderName,
                }
            );
        }
        return voices;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // "en-US-AnaNeural" → "Ana"; "en-US-EmmaMultilingualNeural" → "Emma Multilingual";
    // "iu-Cans-CA-TaqqiqNeural" → "Taqqiq". Strips the locale prefix and the "Neural" suffix, then
    // spaces out camel-cased variant words so multi-word names read naturally.
    private static string DeriveGivenName(string shortName, string locale)
    {
        string name = shortName.StartsWith(locale + "-", StringComparison.OrdinalIgnoreCase)
            ? shortName[(locale.Length + 1)..]
            : shortName;
        if (name.EndsWith("Neural", StringComparison.Ordinal))
            name = name[..^"Neural".Length];
        if (name.Length == 0)
            return shortName;

        StringBuilder spaced = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && char.IsLower(name[i - 1]))
                spaced.Append(' ');
            spaced.Append(name[i]);
        }
        return spaced.ToString();
    }

    /// <summary>Curated subset returned when the live list is unreachable — never an empty catalogue.</summary>
    internal static IReadOnlyList<TtsVoiceInfo> FallbackVoices { get; } =
    [
        new()
        {
            Id = "en-US-AriaNeural",
            Name = "Aria",
            DisplayName = "Aria (US)",
            Locale = "en-US",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "en-US-GuyNeural",
            Name = "Guy",
            DisplayName = "Guy (US)",
            Locale = "en-US",
            Gender = "Male",
            Provider = ProviderName,
        },
        new()
        {
            Id = "en-US-JennyNeural",
            Name = "Jenny",
            DisplayName = "Jenny (US)",
            Locale = "en-US",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "en-US-DavisNeural",
            Name = "Davis",
            DisplayName = "Davis (US)",
            Locale = "en-US",
            Gender = "Male",
            Provider = ProviderName,
        },
        new()
        {
            Id = "en-GB-SoniaNeural",
            Name = "Sonia",
            DisplayName = "Sonia (UK)",
            Locale = "en-GB",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "en-GB-RyanNeural",
            Name = "Ryan",
            DisplayName = "Ryan (UK)",
            Locale = "en-GB",
            Gender = "Male",
            Provider = ProviderName,
        },
        new()
        {
            Id = "en-AU-NatashaNeural",
            Name = "Natasha",
            DisplayName = "Natasha (AU)",
            Locale = "en-AU",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "en-CA-ClaraNeural",
            Name = "Clara",
            DisplayName = "Clara (CA)",
            Locale = "en-CA",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "de-DE-KatjaNeural",
            Name = "Katja",
            DisplayName = "Katja (DE)",
            Locale = "de-DE",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "de-DE-ConradNeural",
            Name = "Conrad",
            DisplayName = "Conrad (DE)",
            Locale = "de-DE",
            Gender = "Male",
            Provider = ProviderName,
        },
        new()
        {
            Id = "fr-FR-DeniseNeural",
            Name = "Denise",
            DisplayName = "Denise (FR)",
            Locale = "fr-FR",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "es-ES-ElviraNeural",
            Name = "Elvira",
            DisplayName = "Elvira (ES)",
            Locale = "es-ES",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "ja-JP-NanamiNeural",
            Name = "Nanami",
            DisplayName = "Nanami (JP)",
            Locale = "ja-JP",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "ko-KR-SunHiNeural",
            Name = "SunHi",
            DisplayName = "SunHi (KR)",
            Locale = "ko-KR",
            Gender = "Female",
            Provider = ProviderName,
        },
        new()
        {
            Id = "pt-BR-FranciscaNeural",
            Name = "Francisca",
            DisplayName = "Francisca (BR)",
            Locale = "pt-BR",
            Gender = "Female",
            Provider = ProviderName,
        },
    ];

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private string BuildConfigMessage(string connectionId)
    {
        string timestamp = _timeProvider
            .GetUtcNow()
            .UtcDateTime.ToString(
                "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'"
            );
        return $"X-Timestamp:{timestamp}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n"
            + JsonSerializer.Serialize(
                new
                {
                    context = new
                    {
                        synthesis = new
                        {
                            audio = new
                            {
                                metadataoptions = new
                                {
                                    sentenceBoundaryEnabled = false,
                                    wordBoundaryEnabled = false,
                                },
                                outputFormat = "audio-24khz-48kbitrate-mono-mp3",
                            },
                        },
                    },
                }
            );
    }

    private string BuildSynthesisMessage(string requestId, string ssml)
    {
        string timestamp = _timeProvider
            .GetUtcNow()
            .UtcDateTime.ToString(
                "ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'"
            );
        return $"X-RequestId:{requestId}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{timestamp}\r\nPath:ssml\r\n\r\n{ssml}";
    }

    private static string BuildSsml(string text, string voiceId)
    {
        string escaped = System.Security.SecurityElement.Escape(text) ?? text;
        return $"""
            <speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
              <voice name='{voiceId}'>
                <prosody rate='+0%' pitch='+0Hz'>{escaped}</prosody>
              </voice>
            </speak>
            """;
    }

    private static async Task SendTextAsync(
        ClientWebSocket ws,
        string message,
        CancellationToken ct
    )
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static int FindHeaderEnd(byte[] data)
    {
        for (int i = 0; i < data.Length - 3; i++)
        {
            if (
                data[i] == '\r'
                && data[i + 1] == '\n'
                && data[i + 2] == '\r'
                && data[i + 3] == '\n'
            )
                return i;
        }
        return -1;
    }

    private static TtsSynthesisResult EmptyResult(string voiceId) =>
        new()
        {
            AudioData = [],
            DurationMs = 0,
            Provider = ProviderName,
            VoiceId = voiceId,
            ContentHash = string.Empty,
        };
}
