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

using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// Giving the music back ends when Spotify reports playing, not when a command was sent (Stoney, 2026-11-09:
/// Spotify must not break while he streams). Until then the director retries, then shows a visible Blocked.
/// </summary>
public sealed partial class PlaybackDirector
{
    private const string SpotifyReturnInput = "SpotifyReturnRetry";
    private const string SpotifyResumeFailed = "SpotifyResumeFailed";

    /// <summary>The wait after each send; its length is the most sends one return gets.</summary>
    private static readonly TimeSpan[] SpotifyReturnWaits =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(8),
    ];

    /// <summary>A Skip is owed only while it threw: repeating an accepted Skip would lose a song.</summary>
    private sealed record PendingSpotifyReturn(
        int Sends,
        DateTimeOffset NextAt,
        bool SkipOwed,
        string? Refusal
    );

    private PendingSpotifyReturn? _spotifyReturn;

    /// <summary>True while Blocked shows a failed return; the poll tick must not hide that error.</summary>
    private bool _spotifyReturnGaveUp;

    private async Task SendSpotifyReturnAsync(int send, bool skip, CancellationToken ct)
    {
        IPlaybackEngine spotify = _engines[EngineKind.Spotify];
        bool failed = false;
        string? refusal = null;
        try
        {
            await (skip ? spotify.SkipAsync(ct) : spotify.ResumeAsync(ct));
            MarkSent(EngineKind.Spotify);
        }
        catch (PlaybackEngineBlockedException blocked)
        {
            failed = true;
            refusal = blocked.Reason.ToString();
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            failed = true;
        }

        _spotifyReturn = new(
            send,
            _time.GetUtcNow() + SpotifyReturnWaits[send - 1],
            skip && failed,
            refusal
        );
    }

    /// <summary>A playing Spotify ends the return; so does the owner, whose own choice is never undone.</summary>
    private async Task ObserveSpotifyReturnAsync(PlaybackInput input, CancellationToken ct)
    {
        _spotifyReturnGaveUp &= State.Kind == PlaybackStateKind.Blocked;
        bool ended =
            input
            is EngineReport { Engine: EngineKind.Spotify, IsPlaying: true }
                or OwnerPause
                or OwnerSkip
                or OwnerPrevious;
        if (_spotifyReturn is not null && ended)
            await EndSpotifyReturnAsync(ct);
    }

    private Task EndSpotifyReturnAsync(CancellationToken ct)
    {
        _spotifyReturn = null;

        return ForgetSpotifyHeldAsync(ct);
    }

    private bool SpotifyReturnDue() =>
        _spotifyReturn is { } pending && _time.GetUtcNow() >= pending.NextAt;

    /// <summary>Reads Spotify first, so a Resume is only repeated for a Spotify still not playing.</summary>
    private async Task RetrySpotifyReturnAsync(CancellationToken ct)
    {
        PendingSpotifyReturn pending = _spotifyReturn!;
        EngineReport? found = await ProbeAsync(_engines[EngineKind.Spotify], ct);
        if (found is { IsPlaying: true })
        {
            _reports[EngineKind.Spotify] = found;
            if (State.Item is null)
                _ownerReport = found;
            await EndSpotifyReturnAsync(ct);
            return;
        }

        if (pending.Sends >= SpotifyReturnWaits.Length)
        {
            await GiveUpSpotifyReturnAsync(pending);
            return;
        }

        await SendSpotifyReturnAsync(pending.Sends + 1, pending.SkipOwed, ct);
        await MoveAsync(State, SpotifyReturnInput, "spotify_resume_retry");
    }

    /// <summary>The durable fact stays: only a playing Spotify, or a restart's recovery, ends it.</summary>
    private async Task GiveUpSpotifyReturnAsync(PendingSpotifyReturn pending)
    {
        _spotifyReturn = null;
        await MoveAsync(
            PlaybackState.Blocked(pending.Refusal ?? SpotifyResumeFailed),
            SpotifyReturnInput,
            "spotify_resume_failed"
        );
        _spotifyReturnGaveUp = true;
    }

    /// <summary>
    /// A start failed while the director holds Spotify: the hold ends here, at any step of a run. The
    /// last video's report is dropped, or it would pass for the owner's music. Null when Spotify never reported.
    /// </summary>
    private async Task<EngineReport?> GiveHeldSpotifyBackAsync(CancellationToken ct)
    {
        ReleaseSpotifyHold();
        _reports.Remove(EngineKind.YouTube);
        await SendSpotifyReturnAsync(1, false, ct);

        return _reports.GetValueOrDefault(EngineKind.Spotify) is { } last
            ? last with
            {
                IsPlaying = true,
            }
            : null;
    }

    /// <summary>A video starting now keeps Spotify paused: the return waits for that video's end.</summary>
    private void DeferSpotifyReturnForVideo(PlaybackItem item)
    {
        if (_spotifyReturn is null || item.Engine == EngineKind.Spotify)
            return;

        _spotifyReturn = null;
        _spotifyHeldForYouTube = true;
    }
}
