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

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// The <c>errorData</c> on a <c>NO_ACTIVE_DEVICE</c> playback-control failure: which provider found no player.
/// The fix differs by provider — open the overlay Audio widget for YouTube, start a Spotify device for Spotify —
/// so chat replies read it instead of guessing.
/// </summary>
public sealed record NoActivePlayerRefusal(string Provider);
