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
    private const string TwitchClipSource = "twitch_clip";
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
        if (IsInterruptedClip(item, state))
        {
            // A clip iframe cannot seek, so reloading it replays it from the start forever (every OBS scene
            // switch unloads the source). It already played: finish it, which loads the next item.
            await playback.ReportStateAsync(
                broadcasterId,
                new(
                    item.Id,
                    MediaPlayerStates.Ended,
                    item.DurationSeconds,
                    item.DurationSeconds,
                    state.Volume
                ),
                cancellationToken
            );
            return [];
        }

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

    // Started (a position) or unknown to this process (a restart lost its state): either way it began before.
    private static bool IsInterruptedClip(MediaShareRequestDto item, MediaPlayerStateDto state) =>
        item.SourceType == TwitchClipSource
        && (state.RequestId != item.Id || state.PositionSeconds > 0);

    private static MediaShareCommandWidgetPayload Command(
        string command,
        int? position = null,
        int? volume = null
    ) => new(command, null, null, null, null, null, null, position, volume);
}
