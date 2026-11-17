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

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// <c>youtube.volume</c>: the overlay player plays at this volume (0-100) from now on, for this video and the ones
/// after it. With <c>fadeMs</c> above 0 the player ramps to it over that time; a fade to 0 holds the silence until
/// the next video and leaves the level the streamer set.
/// </summary>
public sealed record YouTubeVolumeWidgetPayload(int Percent, int FadeMs = 0);
