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

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

public sealed partial class PlaybackDirector
{
    /// <summary>
    /// The head the engine refused for want of an output (no Spotify device, no YouTube player), and what was
    /// true then: the queue and the device the engine had last reported. Null when no head waits.
    /// </summary>
    private OutputWait? _outputWait;

    /// <summary>Why the director is blocked, as a <see cref="PlaybackBlockedReason"/> name; null when it is not.</summary>
    public string? BlockedReason => PlaybackBlockedReason.Of(State, _outputWait?.Engine);

    /// <summary>Starts the quiet wait. The Blocked state and its journal entry were written by the caller.</summary>
    private void BeginOutputWait(PlaybackItem item) =>
        _outputWait = new(
            item.Engine,
            CurrentQueueMark(),
            _reports.GetValueOrDefault(item.Engine)?.DeviceId
        );

    private QueueMark CurrentQueueMark() => new(_queue.Head?.Id, _queue.Snapshot.Count);

    /// <summary>
    /// The one wait-for-signal rule. While a head waits for an output, a poll tick changes nothing and starts
    /// nothing. Four signals change the answer: the queue changed, the player page attached, the streamer
    /// pressed play, or the engine reported a device other than the one that failed. Returns the journal reason
    /// of the signal, or null when this input is not one of them.
    /// </summary>
    private string? OutputWaitSignal(PlaybackInput input, OutputWait wait) =>
        input switch
        {
            RequestAdded when CurrentQueueMark() != wait.Mark => "request_added",
            OutputAttached => "output_attached",
            OwnerResume => "owner_resume",
            EngineReport r when r.Engine == wait.Engine && IsNewDevice(r, wait) =>
                "output_available",
            _ => null,
        };

    private static bool IsNewDevice(EngineReport report, OutputWait wait) =>
        report.DeviceId is not null && report.DeviceId != wait.DeviceId;

    /// <summary>
    /// Ends the wait. A signal about the output itself (not a queue change) also lifts the engine's own pause,
    /// so the start that follows reaches the provider.
    /// </summary>
    private void EndOutputWait(OutputWait wait, string reason)
    {
        _outputWait = null;
        if (
            reason != "request_added"
            && _engines.TryGetValue(wait.Engine, out IPlaybackEngine? engine)
        )
            engine.ForgetOutputBlock();
    }

    private sealed record OutputWait(EngineKind Engine, QueueMark Mark, string? DeviceId);
}
