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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Public SR-page tokens (music-sr.md §3.7) over the <c>Channels.SongRequestPageToken</c> column. Mints a
/// cryptographically-random, URL-safe, non-PII token lazily, rotates it, and resolves it to the channel context the
/// anonymous <c>/sr/{token}</c> page renders. The queue-open + enabled-providers facts come from the music config.
/// </summary>
public sealed class SongRequestPageTokenService(
    IApplicationDbContext db,
    IMusicConfigService musicConfig
) : ISongRequestPageTokenService
{
    public async Task<Result<SongRequestPageDto>> ResolveAsync(
        string pageToken,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(pageToken))
            return Result.Failure<SongRequestPageDto>("Unknown song-request page.", "NOT_FOUND");

        Channel? channel = await db
            .Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SongRequestPageToken == pageToken, cancellationToken);
        return await ProjectAsync(channel, cancellationToken);
    }

    public async Task<Result<SongRequestPageDto>> ResolveByChannelNameAsync(
        string channelName,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(channelName))
            return Result.Failure<SongRequestPageDto>("Unknown song-request page.", "NOT_FOUND");

        // The shareable name link only works once the operator engaged the SR page (a token exists) —
        // no channel becomes discoverable through a page it never set up.
        string normalized = channelName.TrimStart('@').ToLowerInvariant();
        Channel? channel = await db
            .Channels.AsNoTracking()
            .FirstOrDefaultAsync(
                c =>
                    c.NameNormalized == normalized
                    && c.SongRequestPageToken != null
                    && c.SongRequestPageToken != "",
                cancellationToken
            );
        return await ProjectAsync(channel, cancellationToken);
    }

    private async Task<Result<SongRequestPageDto>> ProjectAsync(
        Channel? channel,
        CancellationToken cancellationToken
    )
    {
        if (channel is null)
            return Result.Failure<SongRequestPageDto>("Unknown song-request page.", "NOT_FOUND");

        Result<MusicConfigDto> config = await musicConfig.GetConfigAsync(
            channel.Id.ToString(),
            cancellationToken
        );
        bool accepting = config.IsSuccess && config.Value.IsEnabled;
        List<string> providers = [];
        if (config.IsSuccess)
        {
            if (config.Value.AllowSpotify)
                providers.Add("spotify");
            if (config.Value.AllowYouTube)
                providers.Add("youtube");
        }

        return Result.Success(
            new SongRequestPageDto(channel.Id, channel.Name, accepting, providers)
        );
    }

    public async Task<Result<string>> GetOrCreateAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterId,
            cancellationToken
        );
        if (channel is null)
            return Result.Failure<string>("Channel not found.", "NOT_FOUND");

        if (string.IsNullOrEmpty(channel.SongRequestPageToken))
        {
            channel.SongRequestPageToken = NewToken();
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(channel.SongRequestPageToken);
    }

    public async Task<Result<string>> RotateAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterId,
            cancellationToken
        );
        if (channel is null)
            return Result.Failure<string>("Channel not found.", "NOT_FOUND");

        channel.SongRequestPageToken = NewToken();
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(channel.SongRequestPageToken);
    }

    // 48-char opaque, URL-safe, not PII (mirrors the OverlayToken precedent; cryptographically random).
    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}
