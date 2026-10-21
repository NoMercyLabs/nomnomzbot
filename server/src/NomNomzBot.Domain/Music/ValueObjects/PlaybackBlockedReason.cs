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
/// Why the director is blocked, in the words a client shows a reason for. One name per thing the streamer can
/// fix: open Spotify on a device, open the Music Player page in OBS, reconnect, or get Premium.
/// </summary>
public static class PlaybackBlockedReason
{
    public const string NoSpotifyDevice = "noSpotifyDevice";
    public const string NoMusicPlayer = "noMusicPlayer";
    public const string NotConnected = "notConnected";
    public const string PremiumRequired = "premiumRequired";
    public const string Forbidden = "forbidden";
    public const string Other = "other";

    /// <summary>
    /// The reason name for a state; null when the state is not Blocked. A missing output is named by the engine
    /// that waits for it, because a Spotify device and a Music Player page are fixed in different places.
    /// </summary>
    public static string? Of(PlaybackState state, EngineKind? waitingEngine) =>
        state.Kind != PlaybackStateKind.Blocked ? null
        : state.Reason == nameof(EngineBlockReason.NoOutput) ? NoOutputOf(waitingEngine)
        : state.Reason switch
        {
            nameof(EngineBlockReason.NotConnected) => NotConnected,
            nameof(EngineBlockReason.PremiumRequired) => PremiumRequired,
            nameof(EngineBlockReason.Forbidden) => Forbidden,
            _ => Other,
        };

    private static string NoOutputOf(EngineKind? engine) =>
        engine == EngineKind.YouTube ? NoMusicPlayer : NoSpotifyDevice;
}
