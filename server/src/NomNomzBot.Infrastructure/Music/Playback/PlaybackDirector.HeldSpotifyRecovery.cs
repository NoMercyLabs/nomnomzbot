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

/// <summary>
/// A restart during a video must not leave Spotify paused for good (Stoney, 2026-11-09). The takeover
/// writes a durable fact; a new director gives Spotify back unless the page proves the video still plays.
/// </summary>
public sealed partial class PlaybackDirector
{
    private const string HeldRecoveryInput = "HeldSpotifyRecovery";

    /// <summary>An older fact belongs to an earlier stream; music must not start by itself then.</summary>
    private static readonly TimeSpan HeldFactWindow =
        SongRequestQueueRestoreHostedService.FreshnessWindow;

    private bool _heldFactChecked;
    private bool _heldFactStored;

    /// <summary>When the page has had its time to report; null when no recovery waits.</summary>
    private DateTimeOffset? _heldRecoveryDeadline;

    /// <summary>Written before the pause: a kill between the two only costs a harmless Resume.</summary>
    private async Task RememberSpotifyHeldAsync(CancellationToken ct)
    {
        if (_heldSpotify is null)
            return;

        _heldFactChecked = true;
        _heldFactStored = true;
        await _heldSpotify.SaveAsync(_broadcasterId, _time.GetUtcNow(), ct);
    }

    /// <summary>
    /// Starts the item over <paramref name="stopEngine"/>. The fact is written only when the director owes
    /// Spotify a Resume, so the owner's own pause is never undone. True when Spotify was paused for it.
    /// </summary>
    private async Task<bool> BeginStartHoldingSpotifyAsync(
        PlaybackItem head,
        string inputName,
        string reason,
        EngineKind stopEngine,
        CancellationToken ct
    )
    {
        if (
            stopEngine != EngineKind.Spotify
            || _memory is not { Engine: EngineKind.Spotify, WasPlaying: true }
        )
        {
            await BeginStartAsync(head, inputName, reason, stopEngine, ct);
            return false;
        }

        await RememberSpotifyHeldAsync(ct);
        await BeginStartAsync(head, inputName, reason, stopEngine, ct);
        if (_pausedForHandOver == EngineKind.Spotify)
        {
            _spotifyHeldForYouTube = true;
            return true;
        }

        if (_spotifyReturn is null)
            await ForgetSpotifyHeldAsync(ct);

        return false;
    }

    private async Task ForgetSpotifyHeldAsync(CancellationToken ct)
    {
        if (_heldSpotify is null || !_heldFactStored)
            return;

        _heldFactStored = false;
        await _heldSpotify.ClearAsync(_broadcasterId, ct);
    }

    /// <summary>
    /// Only the inputs the registry gates start a recovery, so a standby instance never does.
    /// A playing report ends the wait: a video keeps Spotify held, Spotify itself needs no Resume.
    /// </summary>
    private async Task ObserveHeldRecoveryAsync(PlaybackInput input, CancellationToken ct)
    {
        if (!_heldFactChecked && input is RequestAdded or OutputAttached)
            await LoadHeldFactAsync(ct);

        if (_heldRecoveryDeadline is null || input is not EngineReport { IsPlaying: true } report)
            return;

        _heldRecoveryDeadline = null;
        if (report.Engine == EngineKind.YouTube)
            _spotifyHeldForYouTube = true;
        else
            await ForgetSpotifyHeldAsync(ct);
    }

    private async Task LoadHeldFactAsync(CancellationToken ct)
    {
        _heldFactChecked = true;
        if (_heldSpotify is null)
            return;

        if (await _heldSpotify.HeldSinceAsync(_broadcasterId, ct) is not { } since)
            return;

        _heldFactStored = true;
        DateTimeOffset now = _time.GetUtcNow();
        if (now - since > HeldFactWindow)
            await ForgetSpotifyHeldAsync(ct);
        else
            _heldRecoveryDeadline = now + ReportDeadline;
    }

    private bool HeldRecoveryDue() =>
        _heldRecoveryDeadline is { } deadline && _time.GetUtcNow() >= deadline;

    /// <summary>The queue waits with the recovery, so a request cannot start over the music coming back.</summary>
    private bool WaitsForHeldRecovery(PlaybackInput input) =>
        _heldRecoveryDeadline is not null
        && State.Kind == PlaybackStateKind.Idle
        && input is RequestAdded or OutputAttached;

    /// <summary>No page reported a playing video in time: Spotify gets the music back, once.</summary>
    private async Task FinishHeldRecoveryAsync(CancellationToken ct)
    {
        _heldRecoveryDeadline = null;
        EngineReport? found = await ProbeAsync(_engines[EngineKind.Spotify], ct);
        _probed.Add(EngineKind.Spotify);
        if (found is not null)
            _reports[EngineKind.Spotify] = found;

        if (found is { IsPlaying: true })
            await ForgetSpotifyHeldAsync(ct);
        else
            await ReturnToSpotifyAsync(HeldRecoveryInput, ct);
    }

    /// <summary>The music is with Spotify again, by whichever path, so the hold is over.</summary>
    private Task ForgetFinishedHoldAsync(CancellationToken ct) =>
        _heldRecoveryDeadline is null
        && _spotifyReturn is null
        && State
            is {
                Engine: EngineKind.Spotify,
                Kind: PlaybackStateKind.Playing or PlaybackStateKind.Starting
            }
            ? ForgetSpotifyHeldAsync(ct)
            : Task.CompletedTask;
}
