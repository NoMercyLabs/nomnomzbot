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
/// Stoney, 2026-11-17 (Bamo16's ask): a crossfade between Spotify and YouTube requests, both ways, over the
/// channel's crossfade (<see cref="IPlaybackEngine.HandOverLeadMs"/>, his 3 s). The YouTube page ramps its own
/// volume in one call. Spotify's volume moves in whole-second steps, driven by the deadline tick, so a 3 s
/// fade costs 3 volume calls plus one restore (Spotify's limit is a rolling 30 s window). A fade that cannot
/// run leaves today's hand-over in place and puts every volume back.
/// </summary>
public sealed partial class PlaybackDirector
{
    /// <summary>One Spotify volume step per second at most.</summary>
    private const int CrossfadeStepMs = 1_000;

    /// <summary>A fade shorter than this is not worth its calls: the hand-over stays a plain cut.</summary>
    private const int MinCrossfadeMs = 1_000;

    /// <summary>The fade that runs now. <paramref name="SpotifyRises"/> is true going to Spotify, false going away from it.</summary>
    private sealed record CrossfadeRamp(
        EngineKind Other,
        bool SpotifyRises,
        DateTimeOffset StartedAt,
        int DurationMs,
        int Steps,
        int Level,
        int StepsDone
    );

    private CrossfadeRamp? _crossfade;

    /// <summary>The engine that started at volume 0 for a fade-in and still owes its level; null when none.</summary>
    private EngineKind? _silentHead;

    /// <summary>
    /// The crossfade time between Spotify and the other engine, or 0 when it is not possible: an engine that
    /// cannot fade, a 0 s crossfade, or a muted level.
    /// </summary>
    private int CrossfadeMs(EngineKind from, EngineKind to)
    {
        if (
            from == to
            || (from != EngineKind.Spotify && to != EngineKind.Spotify)
            || !_engines.TryGetValue(from, out IPlaybackEngine? a)
            || !_engines.TryGetValue(to, out IPlaybackEngine? b)
            || !a.CanCrossfade
            || !b.CanCrossfade
            || !_engines.TryGetValue(EngineKind.Spotify, out IPlaybackEngine? spotify)
            || EffectiveVolume() <= 0
        )
            return 0;

        int leadMs = spotify.HandOverLeadMs ?? 0;

        return leadMs >= MinCrossfadeMs ? leadMs : 0;
    }

    private async Task<bool> TrySetVolumeAsync(EngineKind engine, int level)
    {
        try
        {
            await _engines[engine].SetVolumeAsync(level);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static int StepsFor(int durationMs) => Math.Max(1, durationMs / CrossfadeStepMs);

    /// <summary>Spotify goes to a video: the video starts silent, so the plan can fade it in when it is audible.</summary>
    private async Task StartHeadSilentAsync(EngineKind playing, PlaybackItem head)
    {
        if (CrossfadeMs(playing, head.Engine) > 0 && await TrySetVolumeAsync(head.Engine, 0))
            _silentHead = head.Engine;
    }

    /// <summary>
    /// The silent head is audible while Spotify still plays: ramp it up and Spotify down. False when no fade
    /// runs, so the caller cuts Spotify at once; a ramp that already runs also answers true.
    /// </summary>
    private async Task<bool> BeginCrossfadeInAsync(
        PlaybackItem head,
        PlaybackInput input,
        CancellationToken ct
    )
    {
        if (_crossfade is not null)
        {
            await IgnoreAsync(input);

            return true;
        }

        if (_silentHead != head.Engine || _cutPending != EngineKind.Spotify)
            return false;

        DateTimeOffset now = _time.GetUtcNow();
        int fadeMs = Math.Min(
            CrossfadeMs(EngineKind.Spotify, head.Engine),
            (int)(_cutDeadline - now).TotalMilliseconds
        );
        int level = EffectiveVolume();
        if (fadeMs < MinCrossfadeMs || !await TryFadeAsync(head.Engine, level, fadeMs, ct))
            return false;

        _silentHead = null;
        _crossfade = new(head.Engine, false, now, fadeMs, StepsFor(fadeMs), level, 0);
        await MoveAsync(State, input.GetType().Name, "crossfade_start", head.Id);

        return true;
    }

    /// <summary>A video ends and Spotify comes back: Spotify resumes silent and rises while the page fades the video out.</summary>
    private async Task BeginCrossfadeOutAsync(CancellationToken ct)
    {
        int fadeMs = CrossfadeMs(EngineKind.YouTube, EngineKind.Spotify);
        int level = EffectiveVolume();
        if (fadeMs < MinCrossfadeMs || !await TrySetVolumeAsync(EngineKind.Spotify, 0))
            return;

        if (!await TryFadeAsync(EngineKind.YouTube, 0, fadeMs, ct))
        {
            await TrySetVolumeAsync(EngineKind.Spotify, level);

            return;
        }

        _crossfade = new(
            EngineKind.YouTube,
            true,
            _time.GetUtcNow(),
            fadeMs,
            StepsFor(fadeMs),
            level,
            0
        );
    }

    private async Task<bool> TryFadeAsync(
        EngineKind engine,
        int level,
        int durationMs,
        CancellationToken ct
    )
    {
        try
        {
            await _engines[engine].FadeVolumeAsync(level, durationMs, ct);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Runs on every input and tick: the Spotify steps that are due, the end of the ramp, a head that never fades in.</summary>
    private async Task DriveCrossfadeAsync(CancellationToken ct)
    {
        if (_crossfade is { } ramp)
        {
            await StepCrossfadeAsync(ramp, ct);

            return;
        }

        // A silent head whose start failed or never came: its level goes back at once.
        if (
            _silentHead is { } silent
            && !(State.Kind == PlaybackStateKind.Starting && State.Engine == silent)
        )
        {
            _silentHead = null;
            await TrySetVolumeAsync(silent, EffectiveVolume());
        }
    }

    private async Task StepCrossfadeAsync(CrossfadeRamp ramp, CancellationToken ct)
    {
        double elapsedMs = (_time.GetUtcNow() - ramp.StartedAt).TotalMilliseconds;
        int stepMs = ramp.DurationMs / ramp.Steps;
        int due = Math.Min(ramp.Steps, (int)(elapsedMs / stepMs));
        if (due > ramp.StepsDone)
        {
            int share = ramp.SpotifyRises ? due : ramp.Steps - due;
            if (!await TrySetVolumeAsync(EngineKind.Spotify, ramp.Level * share / ramp.Steps))
            {
                await EndCrossfadeAsync(ramp, abandon: true, ct);

                return;
            }

            _crossfade = ramp with { StepsDone = due };
        }

        if (due >= ramp.Steps)
            await EndCrossfadeAsync(ramp, abandon: false, ct);
    }

    /// <summary>
    /// Going away from Spotify the song is cut now and the video is the playing song; going to Spotify the
    /// last step already restored the level. A failed fade puts both levels back first.
    /// </summary>
    private async Task EndCrossfadeAsync(CrossfadeRamp ramp, bool abandon, CancellationToken ct)
    {
        if (ramp.SpotifyRises)
        {
            _crossfade = null;
            if (abandon)
                await TrySetVolumeAsync(EngineKind.Spotify, ramp.Level);

            return;
        }

        if (abandon)
            _crossfade = ramp with { StepsDone = 0 };

        await CutForHandOverAsync("Crossfade", movedOn: false, ct);
        await SettleCrossfadeAsync();
        if (
            State is { Kind: PlaybackStateKind.Starting, Item: { } item }
            && _reports.TryGetValue(item.Engine, out EngineReport? report)
            && report.IsPlaying
            && report.Uri == item.Uri
        )
            await PlayRowAsync(item, report, "started");
    }

    /// <summary>
    /// Spotify was cut: its volume goes back to the owner level, and a video that was still fading in jumps to
    /// it. Safe to call at any time; it does nothing when no fade or silent start is owed.
    /// </summary>
    private async Task SettleCrossfadeAsync()
    {
        CrossfadeRamp? ramp = _crossfade;
        EngineKind? silent = _silentHead;
        _crossfade = null;
        _silentHead = null;
        if (ramp is not null)
        {
            if (!ramp.SpotifyRises && ramp.StepsDone < ramp.Steps)
                await TrySetVolumeAsync(ramp.Other, ramp.Level);

            await TrySetVolumeAsync(EngineKind.Spotify, ramp.Level);
        }

        if (silent is { } engine)
            await TrySetVolumeAsync(engine, EffectiveVolume());
    }
}
