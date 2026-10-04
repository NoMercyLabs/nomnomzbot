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
    /// <summary>True when music is playing now, and false when it is paused or stopped.</summary>
    public required bool IsPlaying { get; init; }

    /// <summary>The title of the current track. Empty when nothing is loaded.</summary>
    public string? TrackName { get; init; }

    /// <summary>The artist of the current track. Empty when nothing is loaded.</summary>
    public string? Artist { get; init; }

    /// <summary>The album of the current track. Empty when the album is not known.</summary>
    public string? Album { get; init; }

    /// <summary>A URL to the cover image of the album. Empty when there is no cover.</summary>
    public string? AlbumArtUrl { get; init; }

    /// <summary>How long the current track is, in milliseconds.</summary>
    public int DurationMs { get; init; }

    /// <summary>How far into the current track the player is, in milliseconds.</summary>
    public int ProgressMs { get; init; }

    /// <summary>The music service that plays the track: spotify or youtube. Empty when it is not known.</summary>
    public string? Provider { get; init; }

    /// <summary>The id of the track at the music service. Empty when nothing is loaded.</summary>
    public string? TrackUri { get; init; }

    /// <summary>The id of the artist at the music service. Empty when it is not known.</summary>
    public string? ArtistId { get; init; }

    /// <summary>Who asked for this track via <c>!sr</c>, or null when the streamer started it themselves
    /// (or nothing in our fair queue matches the currently playing track). S-MUSIC-5b: the same fact
    /// <c>SongCurrentAction</c> already names in the <c>!song</c> chat reply, carried onward so the
    /// standing now-playing overlay can show it too.</summary>
    public string? RequestedBy { get; init; }

    /// <summary>True when shuffle is on.</summary>
    public bool ShuffleEnabled { get; init; }

    /// <summary>How the player repeats music: Off, Track (one track) or Context (the whole album or playlist).</summary>
    public MusicRepeatMode RepeatMode { get; init; }

    /// <summary>The player volume, as a percentage from 0 to 100.</summary>
    public int VolumePercent { get; init; }

    /// <summary>When the bot saw this player state, in UTC time.</summary>
    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>Live per-action permissions (see <see cref="TrackInfo.CanSetShuffle"/>) — all default
    /// permitted, so a provider that never reports them never falsely disables a control.</summary>
    public bool CanSetShuffle { get; init; } = true;

    /// <summary>True when this event reports that the track named by <see cref="TrackUri"/> finished or failed
    /// (YouTube's ENDED or ERROR report), so a consumer that waits for the end of that track can act.</summary>
    public bool TrackEnded { get; init; }

    /// <summary>True when the player lets the bot change the repeat mode.</summary>
    public bool CanSetRepeat { get; init; } = true;

    /// <summary>True when the player lets the bot skip to the next track.</summary>
    public bool CanSkipNext { get; init; } = true;

    /// <summary>True when the player lets the bot go back to the previous track.</summary>
    public bool CanSkipPrevious { get; init; } = true;

    /// <summary>True when the player lets the bot jump to another point in the track.</summary>
    public bool CanSeek { get; init; } = true;

    /// <summary>True when the player lets the bot pause.</summary>
    public bool CanPause { get; init; } = true;

    /// <summary>True when the player lets the bot resume.</summary>
    public bool CanResume { get; init; } = true;
}
