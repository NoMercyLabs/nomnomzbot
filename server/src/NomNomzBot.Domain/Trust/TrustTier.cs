// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
