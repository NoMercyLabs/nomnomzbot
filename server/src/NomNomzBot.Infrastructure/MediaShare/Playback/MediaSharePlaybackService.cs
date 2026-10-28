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
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.MediaShare.Playback;

/// <summary>
/// The server side of the media-share player. The queue rows are the truth for what plays; the overlay is
/// told what to load through <see cref="MediaPlayerCommandIssuedEvent"/> and reports back what it does.
/// </summary>
public sealed class MediaSharePlaybackService(
    IMediaShareService queue,
    IMediaPlayerStateStore store,
    IEventBus eventBus,
    TimeProvider clock
) : IMediaSharePlaybackService
{
    private const string LoadCommand = "load";

    private static readonly PaginationParams OnePage = new(1, 1);

    public async Task<Result<MediaPlayerStateDto>> SendCommandAsync(
        Guid broadcasterId,
        MediaPlaybackCommandRequest request,
        CancellationToken ct = default
    ) =>
        request.Command switch
        {
            MediaPlaybackCommands.Play => await PlayAsync(broadcasterId, ct),
            MediaPlaybackCommands.Pause => await PassThroughAsync(broadcasterId, "pause", ct),
            MediaPlaybackCommands.Stop => await StopAsync(broadcasterId, ct),
            MediaPlaybackCommands.Skip => await SkipAsync(broadcasterId, ct),
            MediaPlaybackCommands.Seek => await SeekAsync(
                broadcasterId,
                request.PositionSeconds,
                ct
            ),
            MediaPlaybackCommands.Volume => await VolumeAsync(broadcasterId, request.Volume, ct),
            MediaPlaybackCommands.Replay => await ReplayAsync(broadcasterId, request.RequestId, ct),
            MediaPlaybackCommands.PlayNow => await PlayNowAsync(
                broadcasterId,
                request.RequestId,
                ct
            ),
            _ => Result.Failure<MediaPlayerStateDto>(
                $"Unknown playback command '{request.Command}'.",
                "VALIDATION_FAILED"
            ),
        };

    public MediaPlayerStateDto GetState(Guid broadcasterId) => store.Get(broadcasterId);

    public async Task<Result> ReportStateAsync(
        Guid broadcasterId,
        MediaPlayerStateReport report,
        CancellationToken ct = default
    )
    {
        if (
            report.State
            is not (
                MediaPlayerStates.Idle
                or MediaPlayerStates.Playing
                or MediaPlayerStates.Paused
                or MediaPlayerStates.Ended
                or MediaPlayerStates.Error
            )
        )
            return Result.Failure($"Unknown player state '{report.State}'.", "VALIDATION_FAILED");

        MediaShareRequestDto? current = await CurrentAsync(broadcasterId, ct);
        MediaShareRequestDto? item =
            current is not null && current.Id == report.RequestId ? current : null;

        await SetAndPublishAsync(
            broadcasterId,
            new(
                report.State,
                report.RequestId,
                item?.Title,
                item?.RequesterDisplayName,
                item?.ThumbnailUrl,
                report.PositionSeconds,
                report.DurationSeconds,
                report.Volume
            ),
            ct
        );

        if (
            item is null
            || report.State is not (MediaPlayerStates.Ended or MediaPlayerStates.Error)
        )
            return Result.Success();

        if (report.State == MediaPlayerStates.Ended)
            await queue.MarkPlayedAsync(broadcasterId, item.Id, ct);
        else
            await queue.SkipAsync(broadcasterId, item.Id, ct);

        await LoadNextOrIdleAsync(broadcasterId, ct);
        return Result.Success();
    }

    private async Task<Result<MediaPlayerStateDto>> PlayAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        MediaShareRequestDto? current = await CurrentAsync(broadcasterId, ct);
        MediaPlayerStateDto state = store.Get(broadcasterId);
        if (
            current is not null
            && state.State == MediaPlayerStates.Paused
            && state.RequestId == current.Id
        )
            return await PassThroughAsync(broadcasterId, "play", ct);

        MediaShareRequestDto? loaded = await LoadNextAsync(broadcasterId, ct);
        return loaded is null
            ? Result.Failure<MediaPlayerStateDto>("The media queue is empty.", "NOT_FOUND")
            : Result.Success(store.Get(broadcasterId));
    }

    private async Task<Result<MediaPlayerStateDto>> PassThroughAsync(
        Guid broadcasterId,
        string command,
        CancellationToken ct
    )
    {
        await IssueAsync(broadcasterId, command, ct: ct);
        return Result.Success(store.Get(broadcasterId));
    }

    private async Task<Result<MediaPlayerStateDto>> StopAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        MediaShareRequestDto? current = await CurrentAsync(broadcasterId, ct);
        if (current is not null)
            await queue.MarkPlayedAsync(broadcasterId, current.Id, ct);

        await IssueAsync(broadcasterId, "stop", ct: ct);
        await GoIdleAsync(broadcasterId, ct);
        return Result.Success(store.Get(broadcasterId));
    }

    private async Task<Result<MediaPlayerStateDto>> SkipAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        MediaShareRequestDto? current = await CurrentAsync(broadcasterId, ct);
        if (current is not null)
            await queue.SkipAsync(broadcasterId, current.Id, ct);

        await LoadNextOrIdleAsync(broadcasterId, ct, alwaysStop: true);
        return Result.Success(store.Get(broadcasterId));
    }

    private async Task<Result<MediaPlayerStateDto>> SeekAsync(
        Guid broadcasterId,
        int? positionSeconds,
        CancellationToken ct
    )
    {
        if (positionSeconds is null or < 0)
            return Result.Failure<MediaPlayerStateDto>(
                "Seek needs a position of 0 seconds or more.",
                "VALIDATION_FAILED"
            );

        await IssueAsync(broadcasterId, "seek", positionSeconds, ct: ct);
        return Result.Success(store.Get(broadcasterId));
    }

    private async Task<Result<MediaPlayerStateDto>> VolumeAsync(
        Guid broadcasterId,
        int? volume,
        CancellationToken ct
    )
    {
        if (volume is null or < 0 or > 100)
            return Result.Failure<MediaPlayerStateDto>(
                "Volume must be between 0 and 100.",
                "VALIDATION_FAILED"
            );

        await IssueAsync(broadcasterId, "volume", volume: volume, ct: ct);
        store.Set(broadcasterId, store.Get(broadcasterId) with { Volume = volume.Value });
        return Result.Success(store.Get(broadcasterId));
    }

    private async Task<Result<MediaPlayerStateDto>> ReplayAsync(
        Guid broadcasterId,
        Guid? requestId,
        CancellationToken ct
    )
    {
        if (requestId is null)
            return await SeekAsync(broadcasterId, 0, ct);

        Result<MediaShareRequestDto> replayed = await queue.ReplayAsync(
            broadcasterId,
            requestId.Value,
            ct
        );
        if (replayed.IsFailure)
            return Result.Failure<MediaPlayerStateDto>(replayed.ErrorMessage!, replayed.ErrorCode);

        if (await CurrentAsync(broadcasterId, ct) is null)
            await LoadNextAsync(broadcasterId, ct);

        return Result.Success(store.Get(broadcasterId));
    }

    private async Task<Result<MediaPlayerStateDto>> PlayNowAsync(
        Guid broadcasterId,
        Guid? requestId,
        CancellationToken ct
    )
    {
        if (requestId is null)
            return Result.Failure<MediaPlayerStateDto>(
                "play_now needs the id of an approved item.",
                "VALIDATION_FAILED"
            );

        Result<MediaShareRequestDto> moved = await queue.ReorderAsync(
            broadcasterId,
            requestId.Value,
            1,
            ct
        );
        if (moved.IsFailure)
            return Result.Failure<MediaPlayerStateDto>(moved.ErrorMessage!, moved.ErrorCode);

        MediaShareRequestDto? current = await CurrentAsync(broadcasterId, ct);
        if (current is not null)
            await queue.SkipAsync(broadcasterId, current.Id, ct);

        await LoadNextAsync(broadcasterId, ct);
        return Result.Success(store.Get(broadcasterId));
    }

    private async Task<MediaShareRequestDto?> CurrentAsync(Guid broadcasterId, CancellationToken ct)
    {
        Result<PagedList<MediaShareRequestDto>> playing = await queue.GetQueueAsync(
            broadcasterId,
            new(MediaShareStatus.Playing),
            OnePage,
            ct
        );
        return playing.IsSuccess ? playing.Value.Items.FirstOrDefault() : null;
    }

    /// <summary>Takes the next approved item (or the one already playing) and tells the overlay to load it.</summary>
    private async Task<MediaShareRequestDto?> LoadNextAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        Result<MediaShareRequestDto?> next = await queue.GetNextAsync(broadcasterId, ct);
        if (next.IsFailure || next.Value is null)
            return null;

        MediaShareRequestDto item = next.Value;
        await eventBus.PublishAsync(
            new MediaPlayerCommandIssuedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = clock.GetUtcNow(),
                Command = LoadCommand,
                RequestId = item.Id,
                SourceType = item.SourceType,
                MediaRef = item.MediaRef,
                SourceUrl = item.SourceUrl,
                Title = item.Title,
                DurationSeconds = item.DurationSeconds,
            },
            ct
        );
        await SetAndPublishAsync(
            broadcasterId,
            new(
                MediaPlayerStates.Playing,
                item.Id,
                item.Title,
                item.RequesterDisplayName,
                item.ThumbnailUrl,
                0,
                item.DurationSeconds,
                store.Get(broadcasterId).Volume
            ),
            ct
        );
        return item;
    }

    /// <summary>
    /// Loads the next item, or, with an empty queue, goes idle. A stop command goes out when the overlay
    /// may still show an item (<paramref name="alwaysStop"/>, or the stored state is not idle).
    /// </summary>
    private async Task LoadNextOrIdleAsync(
        Guid broadcasterId,
        CancellationToken ct,
        bool alwaysStop = false
    )
    {
        if (await LoadNextAsync(broadcasterId, ct) is not null)
            return;

        if (alwaysStop)
            await IssueAsync(broadcasterId, "stop", ct: ct);

        await GoIdleAsync(broadcasterId, ct);
    }

    private Task GoIdleAsync(Guid broadcasterId, CancellationToken ct) =>
        SetAndPublishAsync(
            broadcasterId,
            MediaPlayerStateDto.Idle with
            {
                Volume = store.Get(broadcasterId).Volume,
            },
            ct
        );

    private Task IssueAsync(
        Guid broadcasterId,
        string command,
        int? position = null,
        int? volume = null,
        CancellationToken ct = default
    ) =>
        eventBus.PublishAsync(
            new MediaPlayerCommandIssuedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = clock.GetUtcNow(),
                Command = command,
                PositionSeconds = position,
                Volume = volume,
            },
            ct
        );

    private Task SetAndPublishAsync(
        Guid broadcasterId,
        MediaPlayerStateDto state,
        CancellationToken ct
    )
    {
        store.Set(broadcasterId, state);
        return eventBus.PublishAsync(
            new MediaPlayerStateChangedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = clock.GetUtcNow(),
                State = state.State,
                RequestId = state.RequestId,
                Title = state.Title,
                SharerName = state.SharerName,
                ThumbnailUrl = state.ThumbnailUrl,
                PositionSeconds = state.PositionSeconds,
                DurationSeconds = state.DurationSeconds,
                Volume = state.Volume,
            },
            ct
        );
    }
}
