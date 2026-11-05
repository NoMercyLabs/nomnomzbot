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

namespace NomNomzBot.Application.Contracts.Tts;

/// <summary>
/// Lets a scripted, timed overlay sequence (e.g. a BSOD-style reward takeover that schedules several
/// <c>play_tts</c> calls at precomputed offsets) hold the channel's one TTS playback slot for its whole
/// run, so an unrelated TTS request arriving mid-sequence (a shoutout, a chat !tts) cannot insert its clip
/// ahead of or between the sequence's own remaining lines — which would desync the sequence's own
/// wall-clock phase timers from what the shared overlay queue is actually playing (the BSOD TTS-timing
/// bug this follows on from). <see cref="ITtsChannelSerializer"/> already orders concurrent requests
/// correctly relative to EACH OTHER; this adds the missing piece — one caller can reserve its place in
/// that order for longer than a single utterance. Pipeline-exposed as the <c>scene_reserve</c>/
/// <c>scene_release</c> actions; a reservation is scoped to one channel at a time and keyed by an opaque
/// token the holder must echo back, never by a pipeline run id (a single redemption invokes each
/// <c>play_tts</c>/<c>scene_release</c> call as its own ad-hoc one-action run with no shared run id to key
/// on).
/// </summary>
public interface ITtsSceneReservation
{
    /// <summary>
    /// Reserves the channel's TTS slot and returns the token the holder must pass as
    /// <c>TtsSpeakRequest.SceneToken</c> on every <c>play_tts</c> call that belongs to this sequence, and to
    /// <see cref="ReleaseAsync"/>. Replaces any reservation already held for this channel (a stale, expired
    /// one, or a previous sequence that never released) — the newest reserving caller always wins; nothing
    /// queues reservations themselves, only the TTS requests waiting on one. Auto-expires after
    /// <paramref name="maxHold"/> even if never released, so a crashed sequence cannot mute the channel's
    /// TTS forever.
    /// </summary>
    Task<string> ReserveAsync(Guid broadcasterId, TimeSpan maxHold, CancellationToken ct = default);

    /// <summary>
    /// Releases the reservation early. A no-op when <paramref name="token"/> does not match the current
    /// holder (already expired and superseded, or a stale release arriving after the sequence's own retry) —
    /// never clears a DIFFERENT, newer reservation.
    /// </summary>
    Task ReleaseAsync(Guid broadcasterId, string token, CancellationToken ct = default);

    /// <summary>
    /// Returns once the channel carries no active reservation, or the caller already holds the only active
    /// one (<paramref name="callerToken"/> matches) — both cases proceed immediately. Otherwise waits until
    /// the active reservation is released or expires, whichever comes first.
    /// </summary>
    Task WaitForClearAsync(Guid broadcasterId, string? callerToken, CancellationToken ct = default);
}
