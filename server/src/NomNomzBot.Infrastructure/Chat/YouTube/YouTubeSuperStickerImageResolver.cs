// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Contracts.YouTube;

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <inheritdoc cref="IYouTubeSuperStickerImageResolver" />
public sealed class YouTubeSuperStickerImageResolver : IYouTubeSuperStickerImageResolver
{
    private const string CsvUrl =
        "https://youtube.googleapis.com/super_stickers/sticker_ids_to_urls.csv";

    /// <summary>
    /// How long a loaded map is trusted. Google's sticker set is near-static, so this is long — a process
    /// restart re-loads it anyway, and there is no user-visible cost to a day-old sticker id/URL pairing.
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<YouTubeSuperStickerImageResolver> _logger;

    // One in-flight load at a time — without this, a burst of Super Stickers before the first load
    // completes would each fire their own fetch of the same CSV.
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private IReadOnlyDictionary<string, string> _images = new Dictionary<string, string>();
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public YouTubeSuperStickerImageResolver(
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<YouTubeSuperStickerImageResolver> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(
        string stickerId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(stickerId))
            return null;

        await EnsureLoadedAsync(cancellationToken);
        return _images.GetValueOrDefault(stickerId);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_timeProvider.GetUtcNow() - _loadedAt < CacheLifetime)
            return;

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the gate: everyone queued behind the winner would otherwise re-fetch in turn.
            if (_timeProvider.GetUtcNow() - _loadedAt < CacheLifetime)
                return;

            IReadOnlyDictionary<string, string>? loaded = await FetchAsync(cancellationToken);
            if (loaded is null)
            {
                // Keep whatever is already cached, even if stale or empty, and back off for a full
                // lifetime — retrying per sticker while Google is unreachable would just hammer them.
                _loadedAt = _timeProvider.GetUtcNow();
                return;
            }

            _images = loaded;
            _loadedAt = _timeProvider.GetUtcNow();
            _logger.LogInformation(
                "Loaded {Count} YouTube Super Sticker image URLs.",
                loaded.Count
            );
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> FetchAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            HttpClient client = _httpClientFactory.CreateClient(YouTubeSuperStickerHttpClient.Name);
            using HttpResponseMessage response = await client.GetAsync(CsvUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "YouTube Super Sticker CSV: {Status}; alerts degrade to text-only.",
                    (int)response.StatusCode
                );
                return null;
            }

            string csv = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(csv);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A sticker image is decoration. It must never cost the alert or block chat delivery, so every
            // failure here is swallowed to a debug line and the caller simply gets text-only.
            _logger.LogDebug(ex, "YouTube Super Sticker CSV could not be fetched.");
            return null;
        }
    }

    /// <summary>
    /// Internal so the wire shape (<c>sticker_id,url</c> per line, no header) is tested against a real
    /// captured sample. Any malformed or short line is skipped rather than throwing.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Parse(string csv)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);

        using StringReader reader = new(csv);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            int commaIndex = line.IndexOf(',');
            if (commaIndex <= 0 || commaIndex == line.Length - 1)
                continue;

            string id = line[..commaIndex].Trim();
            string url = line[(commaIndex + 1)..].Trim();
            if (id.Length > 0 && url.Length > 0)
                result[id] = url;
        }

        return result;
    }
}
