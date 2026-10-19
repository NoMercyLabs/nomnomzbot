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
/// The one now-playing shape for both engines (music-playback-determinism.md section 2.1). The director
/// builds it from engine reports; widgets and the REST read use it.
/// </summary>
public sealed record DirectorNowPlaying(
    EngineKind Engine,
    string? ItemId,
    string Title,
    IReadOnlyList<string> Artists,
    string? AlbumArtUrl,
    int DurationMs,
    int ProgressMs,
    bool IsPlaying,
    string? RequestedBy,
    int Volume,
    bool Muted,
    PlaybackStateKind State
)
{
    /// <summary>
    /// Builds the shape from the director state and level plus the fields an engine report carries. The item
    /// fields (id, artists) come from the director item only when it is the track the report names. Returns
    /// null when nothing is loaded: no title, or no engine to name.
    /// </summary>
    public static DirectorNowPlaying? Build(
        PlaybackState state,
        PlaybackLevel level,
        string? provider,
        string? trackUri,
        string? title,
        string? artist,
        string? albumArtUrl,
        int durationMs,
        int progressMs,
        bool isPlaying,
        string? requestedBy
    ) => null;
}
