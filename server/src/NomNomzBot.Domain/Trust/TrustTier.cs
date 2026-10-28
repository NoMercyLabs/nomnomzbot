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

namespace NomNomzBot.Domain.Trust;

/// <summary>
/// Trust tier derived from a calculated score.
/// Affects song request permissions and queue priority.
/// </summary>
public enum TrustTier
{
    /// <summary>Score 0–25: require mod approval before queuing.</summary>
    Untrusted = 0,

    /// <summary>Score 26–50: Spotify only, no YouTube.</summary>
    Low = 1,

    /// <summary>Score 51–75: all providers, max 3 per session.</summary>
    Standard = 2,

    /// <summary>Score 76–100: all providers, max 5, priority in queue.</summary>
    Trusted = 3,
}
