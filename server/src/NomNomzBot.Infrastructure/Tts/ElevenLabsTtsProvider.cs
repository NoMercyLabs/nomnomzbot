// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// ElevenLabs TTS provider stub (BYOK).
/// Requires ELEVENLABS_API_KEY configuration.
/// </summary>
public sealed class ElevenLabsTtsProvider : ITtsProvider
{
    private const string ProviderName = "elevenlabs";
    private const string ApiBase = "https://api.elevenlabs.io/v1";

    private readonly HttpClient _http;
    private readonly ILogger<ElevenLabsTtsProvider> _logger;
    private readonly string? _apiKey;

    public ElevenLabsTtsProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<ElevenLabsTtsProvider> logger,
        string? apiKey
    )
    {
        _http = httpClientFactory.CreateClient("elevenlabs-tts");
        _logger = logger;
        _apiKey = apiKey;
    }

    public async Task<TtsSynthesisResult> SynthesizeAsync(
        string text,
        string voiceId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogDebug("ElevenLabs TTS: No API key configured");
            return EmptyResult(voiceId);
        }

        string url = $"{ApiBase}/text-to-speech/{voiceId}";
        string body = JsonSerializer.Serialize(
            new
            {
                text,
                model_id = "eleven_multilingual_v2",
                voice_settings = new { stability = 0.5, similarity_boost = 0.75 },
            }
        );

        HttpRequestMessage request = new(HttpMethod.Post, url);
        request.Headers.Add("xi-api-key", _apiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("ElevenLabs TTS: Request failed {Status}", response.StatusCode);
                return EmptyResult(voiceId);
            }

            byte[] audioData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            int durationMs = (int)(audioData.Length / 16.0 * 1000.0 / 1024.0);
            string hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(text + voiceId))
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ElevenLabs TTS: Synthesis failed");
            return EmptyResult(voiceId);
        }
    }

    public async Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(_apiKey))
            return [];

        HttpRequestMessage request = new(HttpMethod.Get, $"{ApiBase}/voices");
        request.Headers.Add("xi-api-key", _apiKey);

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return [];

            ElevenLabsVoicesResponse? data =
                await response.Content.ReadFromJsonAsync<ElevenLabsVoicesResponse>(
                    cancellationToken: cancellationToken
                );

            return data?.Voices?.Select(MapVoice).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ElevenLabs TTS: Failed to fetch voices");
            return [];
        }
    }

    // Maps one ElevenLabs voice onto the catalogue shape, capturing the metadata the adapter used to discard:
    // preview_url, the top-level description, and the label model (accent / age / gender / use-case → tags).
    private static TtsVoiceInfo MapVoice(ElevenLabsVoice v)
    {
        Dictionary<string, string> labels = v.Labels ?? new(StringComparer.OrdinalIgnoreCase);
        string? useCase =
            labels.GetValueOrDefault("use_case") ?? labels.GetValueOrDefault("use case");

        return new TtsVoiceInfo
        {
            Id = v.VoiceId,
            Name = v.Name,
            DisplayName = v.Name,
            Locale = "en-US",
            Gender = labels.GetValueOrDefault("gender") ?? "unknown",
            Provider = ProviderName,
            Accent = labels.GetValueOrDefault("accent"),
            Age = labels.GetValueOrDefault("age"),
            Description = string.IsNullOrWhiteSpace(v.Description)
                ? labels.GetValueOrDefault("description")
                : v.Description,
            PreviewUrl = v.PreviewUrl,
            Tags = string.IsNullOrWhiteSpace(useCase) ? null : [useCase],
        };
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

    private sealed class ElevenLabsVoicesResponse
    {
        [JsonPropertyName("voices")]
        public List<ElevenLabsVoice>? Voices { get; set; }
    }

    private sealed class ElevenLabsVoice
    {
        [JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = null!;

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("labels")]
        public Dictionary<string, string>? Labels { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("preview_url")]
        public string? PreviewUrl { get; set; }
    }
}
