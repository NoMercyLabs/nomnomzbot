// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <inheritdoc cref="IYouTubeSuperStickerAssetResolver" />
public sealed class YouTubeSuperStickerAssetResolver : IYouTubeSuperStickerAssetResolver
{
    private readonly IYouTubeSuperStickerImageResolver _cdnResolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IChannelAssetService _assets;
    private readonly IApplicationDbContext _db;
    private readonly YouTubeSuperStickerAssetCache _cache;
    private readonly ILogger<YouTubeSuperStickerAssetResolver> _logger;

    public YouTubeSuperStickerAssetResolver(
        IYouTubeSuperStickerImageResolver cdnResolver,
        IHttpClientFactory httpClientFactory,
        IChannelAssetService assets,
        IApplicationDbContext db,
        YouTubeSuperStickerAssetCache cache,
        ILogger<YouTubeSuperStickerAssetResolver> logger
    )
    {
        _cdnResolver = cdnResolver;
        _httpClientFactory = httpClientFactory;
        _assets = assets;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAssetUrlAsync(
        Guid broadcasterId,
        string stickerId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(stickerId))
            return null;

        // A repeat sticker for this channel answers from memory: no CDN lookup, no download, no re-upload.
        if (_cache.TryGet(broadcasterId, stickerId, out string? cachedUrl))
            return cachedUrl;

        string? assetUrl = await DownloadAndStoreAsync(broadcasterId, stickerId, cancellationToken);
        if (assetUrl is not null)
            _cache.Set(broadcasterId, stickerId, assetUrl);

        return assetUrl;
    }

    /// <summary>
    /// Resolves the CDN URL, downloads it once, and stores the bytes through the SAME upload path an
    /// operator's own asset upload takes — inheriting its content sniffing, size cap and per-channel quota.
    /// Every failure degrades to null rather than throwing: a sticker image is decoration.
    /// </summary>
    private async Task<string?> DownloadAndStoreAsync(
        Guid broadcasterId,
        string stickerId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            string? cdnUrl = await _cdnResolver.ResolveAsync(stickerId, cancellationToken);
            if (cdnUrl is null)
                return null;

            // The asset library requires a real actor user for its audit column; the channel owner is the
            // only identity a chat-ingest background path can attribute this to.
            Guid ownerUserId = await _db
                .Channels.Where(c => c.Id == broadcasterId)
                .Select(c => c.OwnerUserId)
                .FirstOrDefaultAsync(cancellationToken);
            if (ownerUserId == Guid.Empty)
                return null;

            HttpClient client = _httpClientFactory.CreateClient(YouTubeSuperStickerHttpClient.Name);
            using HttpResponseMessage response = await client.GetAsync(cdnUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "YouTube sticker image download for {StickerId}: {Status}; alert degrades to text-only.",
                    stickerId,
                    (int)response.StatusCode
                );
                return null;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            using MemoryStream content = new(bytes);

            Result<ChannelAssetDto> uploaded = await _assets.UploadAsync(
                broadcasterId,
                ownerUserId,
                new(
                    AssetNameFor(stickerId),
                    $"YouTube Sticker: {stickerId}",
                    $"{stickerId}.png",
                    content
                ),
                cancellationToken
            );
            if (!uploaded.IsSuccess)
            {
                _logger.LogDebug(
                    "Could not store a channel asset for YouTube sticker {StickerId}: {Error}",
                    stickerId,
                    uploaded.ErrorMessage
                );
                return null;
            }

            return uploaded.Value.Url;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                ex,
                "Could not resolve a channel asset for YouTube sticker {StickerId}.",
                stickerId
            );
            return null;
        }
    }

    /// <summary>
    /// Deterministic per-channel asset name (the upload path's own naming rule: 1-50 chars of letters,
    /// digits, '-' or '_') so a re-run after a cache miss (e.g. a restart) replaces the SAME asset row
    /// rather than accumulating duplicates.
    /// </summary>
    private static string AssetNameFor(string stickerId)
    {
        string slug = new([.. stickerId.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')]);
        string name = "yt-sticker-" + slug;
        return name.Length > 50 ? name[..50] : name;
    }
}
