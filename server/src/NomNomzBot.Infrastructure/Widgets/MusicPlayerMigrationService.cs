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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

public sealed class MusicPlayerMigrationService(
    IApplicationDbContext db,
    IWidgetService widgets,
    ILogger<MusicPlayerMigrationService> logger
) : IMusicPlayerMigrationService
{
    internal const string RetiredAudioName = "TTS Audio";
    internal const string AudioName = "Bot Audio";

    public async Task<MusicPlayerMigrationReport> MigrateAllChannelsAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<Guid> channelIds = await db
            .Channels.IgnoreQueryFilters()
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        int ensured = 0;
        int renamed = 0;
        foreach (Guid channelId in channelIds)
        {
            try
            {
                if (await EnsureMusicPlayerAsync(channelId, cancellationToken))
                    ensured++;
                if (await RenameAudioWidgetAsync(channelId, cancellationToken))
                    renamed++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    ex,
                    "Music Player migration failed for channel {ChannelId}",
                    channelId
                );
            }
        }

        logger.LogInformation(
            "Music Player migration: {Channels} channels checked, {Ensured} Music Player widgets created, {Renamed} Audio widgets renamed",
            channelIds.Count,
            ensured,
            renamed
        );
        return new(channelIds.Count, ensured, renamed);
    }

    /// <summary>True when this run created the row.</summary>
    private async Task<bool> EnsureMusicPlayerAsync(Guid channelId, CancellationToken ct)
    {
        string[] keys = [.. WidgetKeyAliases.KeysFor(WidgetKeyAliases.MusicPlayer)];
        bool existed = await db
            .Widgets.IgnoreQueryFilters()
            .AnyAsync(
                w =>
                    w.BroadcasterId == channelId
                    && w.GalleryItemId != null
                    && db.WidgetGalleryItems.Any(i =>
                        i.Id == w.GalleryItemId && keys.Contains(i.NaturalKey!)
                    ),
                ct
            );
        if (existed)
            return false;

        Result<WidgetDetail> result = await widgets.EnsureSystemWidgetAsync(
            channelId.ToString(),
            WidgetKeyAliases.MusicPlayer,
            ct
        );
        if (result.IsFailure)
            logger.LogWarning(
                "Music Player migration: {ChannelId} could not get a Music Player widget: {Error}",
                channelId,
                result.ErrorMessage
            );
        return result.IsSuccess;
    }

    /// <summary>True when the row still had the retired default name and was renamed.</summary>
    private async Task<bool> RenameAudioWidgetAsync(Guid channelId, CancellationToken ct)
    {
        string[] keys = [.. WidgetKeyAliases.KeysFor(WidgetKeyAliases.Audio)];
        List<Widget> audioRows = await db
            .Widgets.IgnoreQueryFilters()
            .Where(w =>
                w.BroadcasterId == channelId
                && w.Name == RetiredAudioName
                && w.Source == "first_party"
                && w.GalleryItemId != null
                && db.WidgetGalleryItems.Any(i =>
                    i.Id == w.GalleryItemId && keys.Contains(i.NaturalKey!)
                )
            )
            .ToListAsync(ct);
        if (audioRows.Count == 0)
            return false;

        foreach (Widget row in audioRows)
            row.Name = AudioName;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
