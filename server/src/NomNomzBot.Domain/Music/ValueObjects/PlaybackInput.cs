// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>
/// Every input the playback director accepts (music-playback-determinism.md section 2, Inputs).
/// Control surfaces and engines send these; none of them call an engine directly.
/// </summary>
public abstract record PlaybackInput;

/// <summary>An input that comes from an engine and names the track it concerns.</summary>
public abstract record EngineSignal(EngineKind Engine, string Uri) : PlaybackInput;

/// <summary>A state report from an engine (Spotify poll, YouTube player). Same shape for both.</summary>
public sealed record EngineReport(
    EngineKind Engine,
    string Uri,
    bool IsPlaying,
    int ProgressMs,
    int DurationMs,
    string? DeviceId = null
) : EngineSignal(Engine, Uri);

/// <summary>The track on <see cref="EngineSignal.Engine"/> ended.</summary>
public sealed record TrackEnded(EngineKind Engine, string Uri) : EngineSignal(Engine, Uri);

/// <summary>The track on <see cref="EngineSignal.Engine"/> has 3 seconds left (the Music Player page's ENDING).</summary>
public sealed record TrackEnding(EngineKind Engine, string Uri) : EngineSignal(Engine, Uri);

/// <summary>A song request was admitted to the fair queue.</summary>
public sealed record RequestAdded : PlaybackInput;

public sealed record OwnerPause : PlaybackInput;

public sealed record OwnerResume : PlaybackInput;

public sealed record OwnerSkip : PlaybackInput;

public sealed record OwnerPrevious : PlaybackInput;

public sealed record OwnerSeek(int PositionMs) : PlaybackInput;

public sealed record OwnerVolume(int Level) : PlaybackInput;

public sealed record OwnerMute(bool On) : PlaybackInput;

/// <summary>Bot audio (TTS, sounds) starts: the music level drops to <paramref name="Percent"/> of the owner level.</summary>
public sealed record DuckStart(int Percent) : PlaybackInput;

/// <summary>Bot audio ended: the music level returns to the owner level.</summary>
public sealed record DuckEnd : PlaybackInput;

/// <summary>
/// The music widget came online. <paramref name="PageIsPlaying"/> is true when the page reported its video
/// playing before it said ready, so the engine must not reload that video.
/// </summary>
public sealed record OutputAttached(bool PageIsPlaying = false) : PlaybackInput;

/// <summary>The music widget went away.</summary>
public sealed record OutputDetached : PlaybackInput;

/// <summary>The <see cref="PlaybackStateKind.Starting"/> deadline passed.</summary>
public sealed record StartTimeout : PlaybackInput;
