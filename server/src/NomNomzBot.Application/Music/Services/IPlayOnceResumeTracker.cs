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

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// What was playing right before a <c>play_track_once</c> pipeline action interrupted it, so the
/// interrupted context can be put back exactly where it left off once the interrupting track is over.
/// A null <see cref="PriorTrackUri"/> means nothing was playing yet (e.g. the "starting soon" screen
/// before the streamer's playlist ever started) — nothing to resume.
/// </summary>
public sealed record PlayOnceResumeState(
    string InterruptingTrackUri,
    string? PriorTrackUri,
    int PriorProgressMs,
    bool PriorWasPlaying
);

/// <summary>
/// Per-channel, in-memory-only memory of an in-flight "play one track now" interruption
/// (<c>play_track_once</c> pipeline action) — the track it started playing immediately, and the
/// playback state it captured right before doing so. <see cref="TryTake"/> removes the entry
/// atomically, so the resume it feeds (<c>PlayOnceResumeHandler</c>) can only ever fire once per
/// interruption, even if several <see cref="NomNomzBot.Domain.Music.Events.PlaybackStateChangedEvent"/>
/// publishes race each other while the interrupting track is still playing.
/// </summary>
public interface IPlayOnceResumeTracker
{
    /// <summary>Remembers a just-started interruption, replacing anything already remembered for this
    /// channel (a second <c>play_track_once</c> firing before the first resolved wins — last one in
    /// decides what gets resumed).</summary>
    void Remember(Guid broadcasterId, PlayOnceResumeState state);

    /// <summary>Non-destructive read of the remembered interruption, or false when there is none — for a
    /// caller that needs to inspect it (e.g. check whether a state change is the interrupting track's own
    /// expected start) before deciding whether taking it is warranted.</summary>
    bool TryPeek(Guid broadcasterId, out PlayOnceResumeState state);

    /// <summary>Atomically removes and returns the remembered interruption, or false when there is
    /// none — either nothing was ever remembered, or a prior call already took it.</summary>
    bool TryTake(Guid broadcasterId, out PlayOnceResumeState state);
}
