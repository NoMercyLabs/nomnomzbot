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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <inheritdoc />
public sealed class SongRequestIntakeService(
    IMusicConfigService config,
    IMusicService music,
    IEventBus eventBus
) : ISongRequestIntakeService
{
    public async Task<Result<bool>> SetRequestsOpenAsync(
        string broadcasterId,
        bool open,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Result.Failure<bool>("Invalid channel id.", "VALIDATION_FAILED");

        Result<MusicConfigDto> current = await config.GetConfigAsync(
            broadcasterId,
            cancellationToken
        );
        if (current.IsFailure)
            return Result.Failure<bool>(current.ErrorMessage!, current.ErrorCode!);

        if (current.Value.RequestsOpen == open)
            return Result.Success(false);

        Result<MusicConfigDto> updated = await config.UpdateConfigAsync(
            broadcasterId,
            new UpdateMusicConfigDto { RequestsOpen = open },
            cancellationToken
        );
        if (updated.IsFailure)
            return Result.Failure<bool>(updated.ErrorMessage!, updated.ErrorCode!);

        // The queue-changed push is what the viewer-music group (public page, viewer /me) and the
        // dashboard's queue panel refresh on; the config push alone reaches only the dashboard.
        await eventBus.PublishAsync(
            new SongRequestQueueChangedEvent
            {
                BroadcasterId = tenantId,
                Items = music.SnapshotQueue(broadcasterId),
            },
            cancellationToken
        );

        return Result.Success(true);
    }
}
