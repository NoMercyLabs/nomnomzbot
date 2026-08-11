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
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public int DurationMs { get; init; }
    public int PositionMs { get; init; }
    public bool IsPlaying { get; init; }
    public bool ShuffleEnabled { get; init; }
    public required string RepeatMode { get; init; }
    public bool? IsSaved { get; init; }
    public int VolumePercent { get; init; }
    public string? AlbumArtUrl { get; init; }

    /// <summary>Live per-action permissions (see <see cref="NomNomzBot.Domain.Music.Interfaces.TrackInfo.CanSetShuffle"/>).</summary>
    public bool CanSetShuffle { get; init; } = true;
    public bool CanSetRepeat { get; init; } = true;
    public bool CanSkipNext { get; init; } = true;
    public bool CanSkipPrevious { get; init; } = true;
    public bool CanSeek { get; init; } = true;
    public bool CanPause { get; init; } = true;
    public bool CanResume { get; init; } = true;
}
