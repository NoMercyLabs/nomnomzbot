// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

public sealed partial class PlaybackDirector
{
    private async Task OnIdleAsync(PlaybackInput input, CancellationToken ct)
    {
        switch (input)
        {
            case RequestAdded or OutputAttached when _queue.Head is { } head:
                await BeginStartAsync(head, input.GetType().Name, "start_head", null, ct);
                break;
            case EngineReport report:
                await OnReportAsync(report, ct);
                break;
            default:
                await IgnoreAsync(input);
                break;
        }
    }

    private async Task OnPlayingAsync(PlaybackInput input, CancellationToken ct)
    {
        switch (input)
        {
            case EngineReport report:
                await OnReportAsync(report, ct);
                break;
            case TrackEnded ended when IsCurrentTrack(ended):
                await OnTrackEndedAsync(ended, ct);
                break;
            case OwnerPause or OwnerSkip or OwnerPrevious or OwnerSeek or OwnerVolume or OwnerMute:
                await OnOwnerIntentAsync(input, ct);
                break;
            case RequestAdded when State.Item is null:
                if (!await HandOverIfNearEndAsync(nameof(RequestAdded), ct))
                    await MoveAsync(State, input, "waiting_near_end");
                break;
            default:
                await IgnoreAsync(input);
                break;
        }
    }

    private async Task OnBlockedAsync(PlaybackInput input, CancellationToken ct)
    {
        if (input is not (OutputAttached or RequestAdded))
        {
            await IgnoreAsync(input);
            return;
        }

        await MoveAsync(
            PlaybackState.Idle,
            input,
            input is OutputAttached ? "output_attached" : "request_added"
        );
        if (_queue.Head is { } head)
            await BeginStartAsync(head, input.GetType().Name, "start_head", null, ct);
    }

    private bool IsCurrentTrack(TrackEnded ended) =>
        ended.Engine == State.Engine && (State.Item is null || State.Item.Uri == ended.Uri);

    private async Task OnReportAsync(EngineReport report, CancellationToken ct)
    {
        if (!report.IsPlaying)
        {
            if (State.Kind == PlaybackStateKind.Playing && report.Engine != State.Engine)
                await SilenceForeignAsync(report, ct);
            else
                await OnNotPlayingReportAsync(report);

            return;
        }

        // A queued row that plays on its own leaves the queue first, even when another engine holds
        // the owner's music: the row was seen playing, so it is no longer waiting.
        if (FindRow(report) is { } row)
        {
            string reason = row.Id == _queue.Head?.Id ? "played_externally" : "played_out_of_order";
            await PlayRowAsync(row, report, reason);
            return;
        }

        if (State.Kind == PlaybackStateKind.Playing && report.Engine != State.Engine)
        {
            await SilenceForeignAsync(report, ct);
            return;
        }

        if (State.Kind == PlaybackStateKind.Playing && State.Item?.Uri == report.Uri)
            return;

        bool ownerAlready = State is { Kind: PlaybackStateKind.Playing, Item: null };
        _ownerReport = report;
        if (!ownerAlready)
        {
            _memory = null;
            await MoveAsync(PlaybackState.Playing(report.Engine, null), report, "owner_music");
        }

        await HandOverIfNearEndAsync(nameof(EngineReport), ct);
    }

    /// <summary>Owner music is in its last seconds and a request waits: the head takes over.</summary>
    private async Task<bool> HandOverIfNearEndAsync(string inputName, CancellationToken ct)
    {
        if (
            _queue.Head is not { } head
            || _ownerReport is not { DurationMs: > 0 } owner
            || owner.DurationMs - owner.ProgressMs > NearEndMs
        )
            return false;

        bool otherEngine = head.Engine != owner.Engine;
        if (otherEngine)
            _memory = new(owner.Engine, owner.IsPlaying);

        await BeginStartAsync(head, inputName, "hand_over", otherEngine ? owner.Engine : null, ct);
        return true;
    }

    private async Task OnTrackEndedAsync(TrackEnded ended, CancellationToken ct)
    {
        if (_queue.Head is { } next)
        {
            await BeginStartAsync(next, nameof(TrackEnded), "next_item", null, ct);
            return;
        }

        await EndOfQueueAsync(ended.Engine, nameof(TrackEnded), ct);
    }
}
