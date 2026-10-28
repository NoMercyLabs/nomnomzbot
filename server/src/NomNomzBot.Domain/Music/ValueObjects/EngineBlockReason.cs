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

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>
/// Why an engine cannot do what the director asked. One set of reasons for every engine, so the director
/// and the dashboard never branch on which engine said it (music-playback-determinism.md section 11.2).
/// </summary>
public enum EngineBlockReason
{
    /// <summary>No usable connection: no token, or the provider rejected it as dead.</summary>
    NotConnected,

    /// <summary>Nothing to play on: no Spotify device, or no YouTube player (music widget) open.</summary>
    NoOutput,

    /// <summary>The provider refused the call: missing permission.</summary>
    Forbidden,

    /// <summary>The provider refused the call because the account has no plan with playback control (Premium).</summary>
    PremiumRequired,
}
