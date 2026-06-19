// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Application-level TTS service that:
/// - Routes synthesis requests to the appropriate ITtsProvider
/// - Caches synthesized audio by content hash to avoid redundant API calls
/// - Falls back to EdgeTtsProvider if the primary provider fails
/// </summary>
public sealed class TtsService : ITtsService
{
    private readonly IEnumerable<ITtsProvider> _providers;
    private readonly ILogger<TtsService> _logger;

    // In-memory cache: contentHash → audio bytes
    // Production: replace with file cache per spec (TTS_CACHE_PATH)
    private readonly Dictionary<string, byte[]> _cache = new();
    private readonly Lock _cacheLock = new();

    public TtsService(IEnumerable<ITtsProvider> providers, ILogger<TtsService> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    public async Task<TtsResult> SynthesizeAsync(
        string text,
        string voiceId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(text))
            return new([], 0, voiceId, "none");

        string cacheKey = BuildCacheKey(text, voiceId);

        // Check cache
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out byte[]? cached))
            {
                _logger.LogDebug("TTS cache hit for voice {VoiceId}", voiceId);
                int cachedDurationMs = (int)(cached.Length / 16.0 * 1000.0 / 1024.0);
                return new(cached, cachedDurationMs, voiceId, "edge-cached");
            }
        }

        // Determine provider by voice prefix
        ITtsProvider provider = ResolveProvider(voiceId);

        TtsSynthesisResult result;
        try
        {
            result = await provider.SynthesizeAsync(text, voiceId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "TTS synthesis failed for voice {VoiceId}, falling back to Edge TTS",
                voiceId
            );

            // Fall back to Edge TTS
            EdgeTtsProvider? edgeProvider = _providers.OfType<EdgeTtsProvider>().FirstOrDefault();
            if (edgeProvider is null)
                return new([], 0, voiceId, "error");

            result = await edgeProvider.SynthesizeAsync(text, "en-US-AriaNeural", ct);
        }

        if (result.AudioData.Length > 0)
        {
            lock (_cacheLock)
            {
                _cache[cacheKey] = result.AudioData;

                // Evict if cache exceeds 200 entries
                if (_cache.Count > 200)
                {
                    string oldest = _cache.Keys.First();
                    _cache.Remove(oldest);
                }
            }
        }

        return new(result.AudioData, result.DurationMs, voiceId, result.Provider);
    }

    public async Task<IReadOnlyList<TtsVoiceInfo>> GetAvailableVoicesAsync(
        CancellationToken ct = default
    )
    {
        List<TtsVoiceInfo> allVoices = new();

        foreach (ITtsProvider provider in _providers)
        {
            try
            {
                IReadOnlyList<TtsVoiceInfo> voices = await provider.GetVoicesAsync(ct);
                allVoices.AddRange(voices);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "TTS provider {Provider} failed to return voices",
                    provider.GetType().Name
                );
            }
        }

        return allVoices;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private ITtsProvider ResolveProvider(string voiceId)
    {
        // Voice ID naming convention: provider prefix
        // Azure: "en-US-AriaNeural" (Neural voices)
        // ElevenLabs: UUID-formatted voice IDs

        // ElevenLabs: UUIDs (8-4-4-4-12 format)
        if (Guid.TryParse(voiceId, out _))
        {
            ElevenLabsTtsProvider? elevenlabs = _providers
                .OfType<ElevenLabsTtsProvider>()
                .FirstOrDefault();
            if (elevenlabs is not null)
                return elevenlabs;
        }

        // Azure configured separately per BYOK
        AzureTtsProvider? azure = _providers.OfType<AzureTtsProvider>().FirstOrDefault();
        if (azure is not null)
            return azure;

        // Default: Edge TTS (free)
        EdgeTtsProvider? edge = _providers.OfType<EdgeTtsProvider>().FirstOrDefault();
        if (edge is not null)
            return edge;

        return _providers.First();
    }

    private static string BuildCacheKey(string text, string voiceId)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text + "|" + voiceId);
        return Convert.ToHexString(SHA256.HashData(bytes))[..24];
    }
}
