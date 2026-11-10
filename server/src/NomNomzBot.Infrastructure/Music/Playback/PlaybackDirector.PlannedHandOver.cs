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

/// <summary>
/// Stoney, 2026-11-10: "i need the song to be ready to be audible BEFORE it has to be switched and to not
/// have the next audio playing and then paused". An engine that plays on by itself after a track
/// (<see cref="IPlaybackEngine.HandOverLeadMs"/>) is handed over ahead of that track's end: the head on the
/// other engine starts first, and the playing engine is cut when the head is audible, or at the last safe
/// moment. A head that fails to start costs nothing: the playing engine was never touched.
/// </summary>
public sealed partial class PlaybackDirector
{
    /// <summary>Slack for the one-second deadline tick and for the time a pause takes to land.</summary>
    private const int HandOverMarginMs = 1_500;

    /// <summary>The time a start needs to become audible on the other engine.</summary>
    private const int HandOverPrepareMs = 3_000;

    private const string PlannedHandOverInput = "PlannedHandOver";

    private readonly Dictionary<EngineKind, DateTimeOffset> _reportAt = [];

    /// <summary>The engine that still plays while the head starts on the other one; null when no cut is owed.</summary>
    private EngineKind? _cutPending;

    /// <summary>The track that played on the uncut engine when the hand-over was planned.</summary>
    private string? _cutTrackUri;

    /// <summary>The last moment the playing engine can be cut before its next track becomes audible.</summary>
    private DateTimeOffset _cutDeadline;

    /// <summary>Runs after every input and on every deadline tick: first the cut that is due, then a new plan.</summary>
    private async Task DrivePlannedHandOverAsync(CancellationToken ct)
    {
        if (_cutPending is not null)
        {
            if (State.Kind == PlaybackStateKind.Starting && _time.GetUtcNow() >= _cutDeadline)
                await CutForHandOverAsync(PlannedHandOverInput, movedOn: false, ct);

            return;
        }

        if (PlannedHandOverFrom() is not { } playing || _queue.Head is not { } head)
            return;

        int remainingMs = RemainingMs(playing.Report);
        int cutAtMs = playing.LeadMs + HandOverMarginMs;
        if (remainingMs > cutAtMs + HandOverPrepareMs || !await CanStartOnAsync(head.Engine, ct))
            return;

        _memory = new(playing.Report.Engine, true);
        if (playing.Report.Engine == EngineKind.Spotify)
            await RememberSpotifyHeldAsync(ct);

        await BeginStartAsync(head, PlannedHandOverInput, "planned_handover", null, ct);
        if (State.Kind != PlaybackStateKind.Starting || State.Engine == playing.Report.Engine)
            return;

        _cutPending = playing.Report.Engine;
        _cutTrackUri = playing.Report.Uri;
        _cutDeadline =
            _time.GetUtcNow() + TimeSpan.FromMilliseconds(Math.Max(0, remainingMs - cutAtMs));
    }

    /// <summary>
    /// The engine that plays now and will play on by itself, with its last report, while a request for another
    /// engine is the head. Null when no hand-over can be planned.
    /// </summary>
    private (EngineReport Report, int LeadMs)? PlannedHandOverFrom()
    {
        if (
            State is not { Kind: PlaybackStateKind.Playing, Engine: { } engine }
            || !_engines.TryGetValue(engine, out IPlaybackEngine? current)
            || current.HandOverLeadMs is not { } leadMs
            || _queue.Head is not { } head
            || head.Engine == engine
            || !_reports.TryGetValue(engine, out EngineReport? report)
            || !report.IsPlaying
            || report.DurationMs <= 0
            || report.Uri != (State.Item?.Uri ?? _ownerReport?.Uri)
        )
            return null;

        return (report, leadMs);
    }

    /// <summary>What is left of the reported track now: the report ages while no new one arrives.</summary>
    private int RemainingMs(EngineReport report)
    {
        double agedMs = _reportAt.TryGetValue(report.Engine, out DateTimeOffset at)
            ? (_time.GetUtcNow() - at).TotalMilliseconds
            : 0;

        return (int)Math.Max(0, report.DurationMs - report.ProgressMs - agedMs);
    }

    /// <summary>
    /// Takes the music from the engine that still plays. Cut before its track ended, the engine moves on to
    /// its next track when it gets the music back. Cut after it already moved on, that next track goes back
    /// to its start and resumes later.
    /// </summary>
    private async Task CutForHandOverAsync(string inputName, bool movedOn, CancellationToken ct)
    {
        if (_cutPending is not { } engine)
            return;

        _cutPending = null;
        if (!await CommandAsync(engine, inputName, e => e.StopAsync(ct)))
            return;

        MarkSent(engine);
        _pausedForHandOver = engine;
        if (engine == EngineKind.Spotify)
        {
            _spotifyHeldForYouTube = true;
            _heldTrackWasSkipped = !movedOn;
        }

        if (movedOn)
            await CommandAsync(engine, inputName, e => e.SeekAsync(0, ct));

        await MoveAsync(State, inputName, "planned_cut", State.Item?.Id);
    }

    /// <summary>
    /// A report from the engine that still plays while the head starts. The same track is expected and changes
    /// nothing. Another track means the engine moved on early: it is cut at once.
    /// </summary>
    private async Task<bool> OnReportFromUncutEngineAsync(PlaybackInput input, CancellationToken ct)
    {
        if (
            _cutPending is not { } engine
            || input is not EngineReport report
            || report.Engine != engine
        )
            return false;

        if (report.IsPlaying && report.Uri != _cutTrackUri)
            await CutForHandOverAsync(nameof(EngineReport), movedOn: true, ct);
        else
            await IgnoreAsync(input);

        return true;
    }
}
