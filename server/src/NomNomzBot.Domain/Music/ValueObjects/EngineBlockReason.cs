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
