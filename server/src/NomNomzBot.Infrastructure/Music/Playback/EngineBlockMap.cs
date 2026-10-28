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
