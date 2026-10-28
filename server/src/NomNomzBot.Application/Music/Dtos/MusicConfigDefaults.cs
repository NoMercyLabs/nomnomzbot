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

namespace NomNomzBot.Application.Music.Dtos;

/// <summary>
/// The one place each music setting's default is written. A channel with no saved row, and a stored
/// row that lacks a field, both resolve to these values; the API returns them.
/// </summary>
public static class MusicConfigDefaults
{
    public const bool IsEnabled = true;
    public const bool RequestsOpen = true;
    public const string PreferredProvider = "auto";
    public const int MaxQueueSize = 50;
    public const int MaxRequestsPerUser = 5;
    public const int MaxSongSeconds = 360;
    public const bool AllowExplicit = true;
    public const bool AllowYouTube = false;
    public const bool AllowSpotify = true;
    public const bool YouTubeMusicCategoryOnly = true;
    public const string MinTrustLevel = "everyone";
    public const bool BangerAutoCreate = false;
}
