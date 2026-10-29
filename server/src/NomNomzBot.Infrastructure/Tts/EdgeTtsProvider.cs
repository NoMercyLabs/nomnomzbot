// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    /// <summary>Free, keyless Microsoft read-aloud service — always usable, no BYOK gate.</summary>
    public bool IsConfigured => true;

    /// <summary>Same read-aloud surface the synthesis WebSocket uses, so one trusted token serves both.</summary>
    private const string VoiceListUrl =
        "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list?trustedclienttoken=";

    internal const string HttpClientName = "edge-tts";

    private readonly TimeProvider _timeProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EdgeTtsProvider> _logger;

    /// <summary>Handshake tries per synthesis; Microsoft refuses roughly a third of them at random.</summary>
    private const int MaxConnectAttempts = 3;

    private readonly Func<ClientWebSocket, Uri, CancellationToken, Task> _connect;

    /// <summary>How many finished connections stay open for the next line. Three clips run at once from a reader.</summary>
    private const int MaxIdleConnections = 4;

    /// <summary>A connection idle longer than this is not reused; Microsoft closes quiet ones on its side.</summary>
    private static readonly TimeSpan MaxIdleTime = TimeSpan.FromSeconds(25);

    private readonly IdleConnectionPool<EdgeConnection> _idle;

    private readonly EdgeClientIdentity _identity;

    public EdgeTtsProvider(
        TimeProvider timeProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<EdgeTtsProvider> logger,
        IOptions<EdgeTtsOptions> options
    )
        : this(
            timeProvider,
            httpClientFactory,
            logger,
            options,
            static (ws, url, ct) => ws.ConnectAsync(url, ct)
        ) { }

    /// <summary>Test seam: lets a test drive the handshake result without the network.</summary>
    internal EdgeTtsProvider(
        TimeProvider timeProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<EdgeTtsProvider> logger,
        IOptions<EdgeTtsOptions> options,
        Func<ClientWebSocket, Uri, CancellationToken, Task> connect
    )
    {
        _timeProvider = timeProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _connect = connect;
        _identity = EdgeClientIdentity.Resolve(options.Value, out List<string> rejected);
        foreach (string key in rejected)
            _logger.LogError(
                "Edge TTS: the {Section}:{Key} setting is malformed and was ignored; the shipped value is used",
                EdgeTtsOptions.SectionName,
                key
            );
        _idle = new(
            timeProvider,
            MaxIdleConnections,
            MaxIdleTime,
            connection => connection.Socket.State == WebSocketState.Open
        );
    }

    public Task<TtsSynthesisResult> SynthesizeAsync(
        string text,
        string voiceId,
        double? ratePercent = null,
        double? pitchPercent = null,
        CancellationToken cancellationToken = default
    ) =>
        SynthesizeSsmlAsync(
            BuildSsml(text, voiceId, ratePercent, pitchPercent),
            text,
            voiceId,
            cancellationToken
        );

    public Task<TtsSynthesisResult> SynthesizeSegmentsAsync(
        IReadOnlyList<TtsSegment> segments,
        CancellationToken cancellationToken = default
    ) =>
        SynthesizeSsmlAsync(
            BuildSsml(segments),
            string.Join('\n', segments.Select(s => s.Text + s.VoiceId + s.BreakAfterMs)),
            segments[0].VoiceId,
            cancellationToken
        );

    private async Task<TtsSynthesisResult> SynthesizeSsmlAsync(
        string ssml,
        string hashSource,
        string voiceId,
        CancellationToken cancellationToken
    )
    {
        byte[] audioData = [];

        // A kept-open connection skips the handshake, which is most of the wait for a short line. Microsoft
        // closes an idle one without notice, so an empty answer from a reused connection is tried again on a
        // new one.
        if (_idle.TryRent(out EdgeConnection? reused) && reused is not null)
            audioData = await SynthesizeOnAsync(reused, ssml, voiceId, cancellationToken);

        if (audioData.Length == 0)
        {
            EdgeConnection? connection = await ConnectWithRetryAsync(cancellationToken);
            if (connection is null)
                return EmptyResult(voiceId);

            audioData = await SynthesizeOnAsync(connection, ssml, voiceId, cancellationToken);
        }

        if (audioData.Length == 0)
        {
            _logger.LogWarning("Edge TTS: No audio received for voice {VoiceId}", voiceId);
            return EmptyResult(voiceId);
        }

        // The length comes from the MP3 frame headers; 48 kbit/s is the rate requested above.
        int durationMs = Mp3Duration.ToMilliseconds(audioData, 48);
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(hashSource + voiceId))
        )[..16];

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
    /// Speaks one request on a connection and collects the audio. A connection that finished the turn cleanly
    /// goes back to the idle pool; any other is disposed. Returns an empty array when nothing came back.
    /// </summary>
    private async Task<byte[]> SynthesizeOnAsync(
        EdgeConnection connection,
        string ssml,
        string voiceId,
        CancellationToken cancellationToken
    )
    {
        ClientWebSocket ws = connection.Socket;
        using MemoryStream audioStream = new();
        byte[] buffer = new byte[32768];
        bool turnEnded = false;

        try
        {
            // The configuration is sent once per connection, not once per request.
            if (!connection.ConfigSent)
            {
                await SendTextAsync(
                    ws,
                    BuildConfigMessage(connection.ConnectionId),
                    cancellationToken
                );
                connection.ConfigSent = true;
            }

            string requestId = Guid.NewGuid().ToString("N");
            await SendTextAsync(ws, BuildSynthesisMessage(requestId, ssml), cancellationToken);

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
                    int audioStart = FindBinaryAudioStart(msgBytes);
                    if (audioStart >= 0 && audioStart < msgBytes.Length)
                        audioStream.Write(msgBytes, audioStart, msgBytes.Length - audioStart);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    string text2 = Encoding.UTF8.GetString(msgBytes);
                    if (text2.Contains("turn.end"))
                    {
                        turnEnded = true;
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            connection.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Edge TTS: Error receiving audio for voice {VoiceId}", voiceId);
        }

        if (turnEnded)
            _idle.Return(connection);
        else
            await CloseAsync(connection);

        return audioStream.ToArray();
    }

    private static async Task CloseAsync(EdgeConnection connection)
    {
        try
        {
            if (connection.Socket.State == WebSocketState.Open)
                await connection.Socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Done",
                    CancellationToken.None
                );
        }
        catch
        { /* ignore close errors */
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// Opens the read-aloud WebSocket. Microsoft refuses the handshake with 403 at random, even inside one
    /// Sec-MS-GEC window, so a refused try is repeated on a brand-new socket (new ConnectionId, GEC token and
    /// muid cookie), as upstream edge-tts does. Any other failure ends at once. Returns null when it gave up.
    /// </summary>
    internal async Task<EdgeConnection?> ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            string connectionId = Guid.NewGuid().ToString("N");

            // Microsoft rejects the handshake without a Sec-MS-GEC token proving the caller holds the
            // trusted client token as of a recent 5-minute window (see rany2/edge-tts#290).
            Uri url = new(
                $"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1"
                    + $"?TrustedClientToken={_identity.TrustedClientToken}"
                    + $"&Sec-MS-GEC={GenerateSecMsGecToken()}"
                    + $"&Sec-MS-GEC-Version={_identity.SecMsGecVersion}"
                    + $"&ConnectionId={connectionId}"
            );

            ClientWebSocket ws = CreateSocket();
            try
            {
                await _connect(ws, url, cancellationToken);
                return new(ws, connectionId);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                bool refused = IsHandshakeRefused(ws, ex);
                ws.Dispose();

                if (!refused)
                {
                    _logger.LogError(ex, "Edge TTS: Failed to connect");
                    return null;
                }

                if (attempt == MaxConnectAttempts)
                {
                    _logger.LogError(
                        ex,
                        "Edge TTS: Failed to connect, Microsoft refused all {Attempts} handshakes",
                        MaxConnectAttempts
                    );
                    return null;
                }

                _logger.LogWarning(
                    "Edge TTS: handshake refused with 403 (try {Attempt} of {Max}); retrying",
                    attempt,
                    MaxConnectAttempts
                );
                await Task.Delay(
                    TimeSpan.FromMilliseconds(250 * attempt),
                    _timeProvider,
                    cancellationToken
                );
            }
            catch
            {
                ws.Dispose();
                throw;
            }
        }

        return null;
    }

    private static bool IsHandshakeRefused(ClientWebSocket ws, Exception ex) =>
        ws.HttpStatusCode == HttpStatusCode.Forbidden
        || (
            ex is WebSocketException
            && (
                ex.Message.Contains("'403'", StringComparison.Ordinal)
                || ex.InnerException?.Message.Contains("403", StringComparison.Ordinal) == true
            )
        );

    private ClientWebSocket CreateSocket()
    {
        ClientWebSocket ws = new();
        ws.Options.CollectHttpResponseDetails = true;
        // Full browser-fingerprint header set — Microsoft's edge WAF 403s the handshake if any of these
        // are missing or if User-Agent's Chrome/Edg version doesn't match Sec-MS-GEC-Version.
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader(
            "Origin",
            "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold"
        );
        ws.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br, zstd");
        ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        ws.Options.SetRequestHeader("User-Agent", _identity.UserAgent);
        ws.Options.SetRequestHeader(
            "Cookie",
            $"muid={Convert.ToHexString(RandomNumberGenerator.GetBytes(16))};"
        );
        return ws;
    }

    /// <summary>
    /// An open read-aloud socket and the ConnectionId its handshake carried. <see cref="ConfigSent"/> is true once
    /// the speech configuration went over this socket, so a reused connection does not send it again.
    /// </summary>
    internal sealed class EdgeConnection(ClientWebSocket socket, string connectionId) : IDisposable
    {
        public ClientWebSocket Socket { get; } = socket;
        public string ConnectionId { get; } = connectionId;
        public bool ConfigSent { get; set; }

        public void Dispose() => Socket.Dispose();
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
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                VoiceListUrl + _identity.TrustedClientToken
            );
            request.Headers.TryAddWithoutValidation("User-Agent", _identity.UserAgent);
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
                new()
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

    /// <summary>
    /// The v2 Sec-MS-GEC token for the current 5-minute window (<see cref="EdgeSecMsGecToken"/>); Microsoft's
    /// WebSocket handshake 403s without it.
    /// </summary>
    internal string GenerateSecMsGecToken() =>
        EdgeSecMsGecToken.Generate(_timeProvider.GetUtcNow(), _identity.TrustedClientToken);

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

    /// <summary>
    /// Builds the SSML document sent over the Edge TTS WebSocket. Both <paramref name="text"/> and
    /// <paramref name="voiceId"/> are caller-supplied and must be XML-escaped before interpolation — an
    /// unescaped <paramref name="voiceId"/> would otherwise let a caller break out of the attribute and
    /// inject arbitrary SSML markup. <paramref name="ratePercent"/>/<paramref name="pitchPercent"/> are
    /// optional per-call prosody overrides, clamped to <see cref="TtsProsody.MinPercent"/>..<see
    /// cref="TtsProsody.MaxPercent"/> (never trusted verbatim into SSML) and defaulting to <c>+0%</c>.
    /// </summary>
    internal static string BuildSsml(
        string text,
        string voiceId,
        double? ratePercent = null,
        double? pitchPercent = null
    )
    {
        string escaped = System.Security.SecurityElement.Escape(text);
        string escapedVoiceId = System.Security.SecurityElement.Escape(voiceId);
        string rate = TtsProsody.FormatPercent(ratePercent);
        string pitch = TtsProsody.FormatPercent(pitchPercent);
        return $"""
            <speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
              <voice name='{escapedVoiceId}'>
                <prosody rate='{rate}' pitch='{pitch}'>{escaped}</prosody>
              </voice>
            </speak>
            """;
    }

    /// <summary>One <c>&lt;speak&gt;</c> with one <c>&lt;voice&gt;</c> per segment and a <c>&lt;break&gt;</c> between them.</summary>
    internal static string BuildSsml(IReadOnlyList<TtsSegment> segments) =>
        TtsSegmentSsml.Build(segments);

    private static async Task SendTextAsync(
        ClientWebSocket ws,
        string message,
        CancellationToken ct
    )
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    /// <summary>
    /// Binary audio frames are [2-byte big-endian header length][that many header bytes][raw audio] — not
    /// a header terminated by a blank line. Microsoft's current framing puts only a single CRLF between
    /// header lines and none after the last one; searching for a double-CRLF (the old assumption) never
    /// matches, so every frame's audio was silently discarded. Requiring "Path:audio" in the header text
    /// (not just a length that happens to fit) rejects any non-audio binary frame Microsoft ever sends.
    /// </summary>
    private static int FindBinaryAudioStart(byte[] data)
    {
        if (data.Length < 2)
            return -1;
        int headerLength = (data[0] << 8) | data[1];
        int audioStart = 2 + headerLength;
        if (audioStart > data.Length)
            return -1;

        string header = Encoding.UTF8.GetString(data, 2, headerLength);
        return header.Contains("Path:audio", StringComparison.Ordinal) ? audioStart : -1;
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
