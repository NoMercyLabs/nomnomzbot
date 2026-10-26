// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Gives media_share its clip back on join: the playing item is loaded again, moved to the last reported
/// position, set to the stored volume, and paused when the player was paused. Nothing playing gives no frames.
/// </summary>
public sealed class MediaShareSeedProvider(
    IMediaShareService queue,
    IMediaSharePlaybackService playback
) : IWidgetSeedProvider
{
    private const string CommandEvent = "media_share_command";
    private static readonly PaginationParams OnePage = new(1, 1);

    public string NaturalKey => "media_share";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        Result<PagedList<MediaShareRequestDto>> playing = await queue.GetQueueAsync(
            broadcasterId,
            new(MediaShareStatus.Playing),
            OnePage,
            cancellationToken
        );
        MediaShareRequestDto? item = playing.IsSuccess
            ? playing.Value.Items.FirstOrDefault()
            : null;
        if (item is null)
            return [];

        MediaPlayerStateDto state = playback.GetState(broadcasterId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<MediaShareCommandWidgetPayload> commands =
        [
            new(
                "load",
                item.Id,
                item.SourceType,
                item.MediaRef,
                item.SourceUrl,
                item.Title,
                item.DurationSeconds,
                null,
                null
            ),
        ];
        bool sameItem = state.RequestId == item.Id;
        if (sameItem && state.PositionSeconds > 0)
            commands.Add(Command("seek", position: state.PositionSeconds));
        commands.Add(Command("volume", volume: state.Volume));
        if (sameItem && state.State == MediaPlayerStates.Paused)
            commands.Add(Command("pause"));

        return [.. commands.Select(c => new WidgetSeedFrame(CommandEvent, c, now))];
    }

    private static MediaShareCommandWidgetPayload Command(
        string command,
        int? position = null,
        int? volume = null
    ) => new(command, null, null, null, null, null, null, position, volume);
}
