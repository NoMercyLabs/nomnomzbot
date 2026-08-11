// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Music.Events;

/// <summary>
/// Full playback-state snapshot, not just play/pause + name — <see cref="ProgressMs"/> +
/// <see cref="ObservedAt"/> form a position anchor (widget-sdk.md §9: consumers extrapolate
/// <c>positionMs + (now - observedAt)</c> client-side while playing, rather than expecting a per-second
/// push), and <see cref="Artist"/>/<see cref="Album"/>/<see cref="AlbumArtUrl"/>/<see cref="DurationMs"/>
/// let every subscriber (dashboard hub, automation <c>song.changed</c>) render the real track without
/// re-reading it themselves.
/// </summary>
public sealed class PlaybackStateChangedEvent : DomainEventBase
{
    public required bool IsPlaying { get; init; }
    public string? TrackName { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtUrl { get; init; }
    public int DurationMs { get; init; }
    public int ProgressMs { get; init; }
    public string? Provider { get; init; }
    public string? TrackUri { get; init; }
    public string? ArtistId { get; init; }
    public bool ShuffleEnabled { get; init; }
    public MusicRepeatMode RepeatMode { get; init; }
    public int VolumePercent { get; init; }
    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>Live per-action permissions (see <see cref="TrackInfo.CanSetShuffle"/>) — all default
    /// permitted, so a provider that never reports them never falsely disables a control.</summary>
    public bool CanSetShuffle { get; init; } = true;
    public bool CanSetRepeat { get; init; } = true;
    public bool CanSkipNext { get; init; } = true;
    public bool CanSkipPrevious { get; init; } = true;
    public bool CanSeek { get; init; } = true;
    public bool CanPause { get; init; } = true;
    public bool CanResume { get; init; } = true;
}
