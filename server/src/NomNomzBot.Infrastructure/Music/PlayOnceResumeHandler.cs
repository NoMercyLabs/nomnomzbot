// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// The other half of <c>play_track_once</c> (music-automation-controls.md-style "play one track now
/// without touching the auto playlist"): resumes whatever was playing right before the interruption the
/// instant the provider's own playback state shows the interrupting track is over.
///
/// <para>
/// <see cref="PlaybackStateChangedEvent"/> already fires on every real state change the bot didn't
/// itself just cause AND on every mutation this seam issues (<c>MusicStatePollingService</c> for the
/// former, <c>MusicService.PublishPlaybackStateChangedAsync</c> for the latter) — the same event
/// <see cref="SongChangedProjector"/> already turns into the public <c>song.changed</c> automation
/// event. Using it here means no new polling, no fixed "track duration" timer that drifts if Spotify's
/// own progress does: the FIRST state change reporting a track other than the one this handler started
/// is the real "track finished" signal, whether that happened because the track played out naturally or
/// because something else (a moderator's skip, a second play_track_once) moved playback on early.
/// </para>
///
/// <para>
/// The event <see cref="MusicService.PlayTrackOnceAsync"/> itself publishes right after starting the
/// interrupting track (<c>TrackUri == InterruptingTrackUri</c>) must never be read as "already
/// finished" — <see cref="IPlayOnceResumeTracker.TryPeek"/> checks that before
/// <see cref="IPlayOnceResumeTracker.TryTake"/> ever removes the pending entry, so that publish is a
/// no-op here and the resume fires exactly once, on the genuine next change.
/// </para>
/// </summary>
public sealed class PlayOnceResumeHandler : IEventHandler<PlaybackStateChangedEvent>
{
    private readonly IPlayOnceResumeTracker _tracker;
    private readonly IMusicService _music;
    private readonly ILogger<PlayOnceResumeHandler> _logger;

    public PlayOnceResumeHandler(
        IPlayOnceResumeTracker tracker,
        IMusicService music,
        ILogger<PlayOnceResumeHandler> logger
    )
    {
        _tracker = tracker;
        _music = music;
        _logger = logger;
    }

    public async Task HandleAsync(
        PlaybackStateChangedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        if (!_tracker.TryPeek(@event.BroadcasterId, out PlayOnceResumeState pending))
            return;

        // The interrupting track's own expected start — not the resume signal.
        if (string.Equals(@event.TrackUri, pending.InterruptingTrackUri, StringComparison.Ordinal))
            return;

        if (!_tracker.TryTake(@event.BroadcasterId, out pending))
            return; // A racing publish already took it — resume fires exactly once.

        if (pending.PriorTrackUri is null)
            return; // Nothing was playing before the interruption — nothing to put back.

        string broadcasterId = @event.BroadcasterId.ToString();

        Result resumed = await _music.PlayTrackOnceAsync(
            broadcasterId,
            pending.PriorTrackUri,
            cancellationToken
        );
        if (resumed.IsFailure)
        {
            _logger.LogWarning(
                "play_track_once: could not resume the interrupted track for {BroadcasterId}: {Error}",
                @event.BroadcasterId,
                resumed.ErrorMessage
            );
            return;
        }

        // Land back on the exact position it was interrupted at — "exactly where it left off".
        await _music.SeekAsync(broadcasterId, pending.PriorProgressMs, cancellationToken);

        // It was paused when the interruption started (e.g. the streamer had paused their playlist
        // before going live) — resuming it audibly would be a new, unrequested behavior.
        if (!pending.PriorWasPlaying)
            await _music.PauseAsync(broadcasterId, cancellationToken);
    }
}
