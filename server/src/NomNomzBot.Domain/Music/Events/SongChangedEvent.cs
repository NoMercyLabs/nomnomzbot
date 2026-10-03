// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Music.Events;

/// <summary>
/// The position-anchor now-playing shape (music-automation-controls.md D4, widget-sdk.md §9) — the
/// source event <c>SongChangedAutomationEventDescriptor</c> wraps to become the public
/// <c>song.changed</c> automation event. Carries exactly the fields
/// <c>MusicAutomationProjection.ToNowPlayingAsync</c> produces, resolved once at publish time
/// (<c>SongChangedProjector</c>) so every downstream consumer — the automation event stream and the
/// REST now-playing read — renders identical state for identical playback.
/// </summary>
public sealed class SongChangedEvent : DomainEventBase
{
    /// <summary>The title of the current song. Empty when nothing is loaded.</summary>
    public string? Title { get; init; }

    /// <summary>The artist of the current song. Empty when nothing is loaded.</summary>
    public string? Artist { get; init; }

    /// <summary>How long the song is, in milliseconds.</summary>
    public int DurationMs { get; init; }

    /// <summary>How far into the song the player is, in milliseconds.</summary>
    public int PositionMs { get; init; }

    /// <summary>True when music is playing now, and false when it is paused or stopped.</summary>
    public bool IsPlaying { get; init; }

    /// <summary>True when shuffle is on.</summary>
    public bool ShuffleEnabled { get; init; }

    /// <summary>How the player repeats music: off, track (one track) or context (the whole album or playlist).</summary>
    public required string RepeatMode { get; init; }

    /// <summary>True when the song is saved in the streamer's music library. Empty when it is not known.</summary>
    public bool? IsSaved { get; init; }

    /// <summary>The player volume, as a percentage from 0 to 100.</summary>
    public int VolumePercent { get; init; }

    /// <summary>A URL to the cover image of the album. Empty when there is no cover.</summary>
    public string? AlbumArtUrl { get; init; }

    /// <summary>Live per-action permissions (see <see cref="NomNomzBot.Domain.Music.Interfaces.TrackInfo.CanSetShuffle"/>).</summary>
    public bool CanSetShuffle { get; init; } = true;

    /// <summary>True when the player lets the bot change the repeat mode.</summary>
    public bool CanSetRepeat { get; init; } = true;

    /// <summary>True when the player lets the bot skip to the next song.</summary>
    public bool CanSkipNext { get; init; } = true;

    /// <summary>True when the player lets the bot go back to the previous song.</summary>
    public bool CanSkipPrevious { get; init; } = true;

    /// <summary>True when the player lets the bot jump to another point in the song.</summary>
    public bool CanSeek { get; init; } = true;

    /// <summary>True when the player lets the bot pause.</summary>
    public bool CanPause { get; init; } = true;

    /// <summary>True when the player lets the bot resume.</summary>
    public bool CanResume { get; init; } = true;
}
