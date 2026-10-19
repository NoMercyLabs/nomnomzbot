// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// Turns what a music provider throws into the one <see cref="EngineBlockReason"/> both engines share, so a
/// dead Spotify token and a closed YouTube player read the same to the director.
/// </summary>
internal static class EngineBlockMap
{
    /// <summary>The reason a provider failure maps to, or null when it is not a known precondition failure.</summary>
    public static EngineBlockReason? Reason(Exception failure) =>
        failure switch
        {
            NoActiveDeviceException => EngineBlockReason.NoOutput,
            MusicAuthenticationFailedException => EngineBlockReason.NotConnected,
            PremiumRequiredException => EngineBlockReason.PremiumRequired,
            MusicForbiddenException => EngineBlockReason.Forbidden,
            _ => null,
        };
}
