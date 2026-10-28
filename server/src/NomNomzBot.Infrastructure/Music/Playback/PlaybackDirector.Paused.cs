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

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

public sealed partial class PlaybackDirector
{
    private async Task OnPausedAsync(PlaybackInput input, CancellationToken ct)
    {
        EngineKind engine = State.Engine!.Value;
        switch (input)
        {
            case OwnerResume:
                if (!await CommandAsync(engine, nameof(OwnerResume), e => e.ResumeAsync(ct)))
                    break;

                MarkSent(engine);
                await MoveAsync(PlaybackState.Playing(engine, State.Item), input, "owner_resume");
                break;
            case OwnerSkip:
                await OnPausedSkipAsync(engine, input, ct);
                break;
            case OwnerSeek or OwnerVolume or OwnerMute or DuckStart or DuckEnd:
                // The owner can still set the level or the position while paused; the state stays PausedByOwner.
                await OnOwnerIntentAsync(input, ct);
                break;
            case OutputAttached attached:
                await ReattachOutputAsync(engine, attached, ct);
                break;
            case RequestAdded:
                await MoveAsync(State, input, "held_paused");
                break;
            case EngineReport report:
                await OnPausedReportAsync(engine, report, ct);
                break;
            default:
                await IgnoreAsync(input);
                break;
        }
    }

    private async Task OnPausedSkipAsync(
        EngineKind engine,
        PlaybackInput input,
        CancellationToken ct
    )
    {
        if (_queue.Head is { } head)
        {
            if (head.Engine == engine && HasOwnQueue(engine))
            {
                await SkipToHeadAsync(head, input.GetType().Name, ct);
                return;
            }

            if (State.Item is null && head.Engine != engine)
                _memory = new(engine, false);

            await BeginStartAsync(head, input.GetType().Name, "owner_skip", null, ct);
        }
        else if (State.Item is not null)
            await MoveAsync(PlaybackState.Idle, input, "queue_empty");
        else
            await IgnoreAsync(input);
    }

    /// <summary>
    /// Reports while paused: a foreign engine is silenced, a playing report we did not cause is the owner
    /// resuming, everything else is a poll echo and is not journaled.
    /// </summary>
    private async Task OnPausedReportAsync(
        EngineKind engine,
        EngineReport report,
        CancellationToken ct
    )
    {
        if (report.Engine != engine)
        {
            await SilenceForeignAsync(report, ct);
            return;
        }

        if (!report.IsPlaying || SentRecently(engine))
            return;

        if (FindRow(report) is { } row)
        {
            await PlayRowAsync(row, report, "played_out_of_order");
            return;
        }

        PlaybackItem? item = State.Item?.Uri == report.Uri ? State.Item : null;
        if (item is null)
        {
            _ownerReport = report;
            _memory = null;
        }

        await MoveAsync(PlaybackState.Playing(engine, item), report, "owner_resume_detected");
    }
}
