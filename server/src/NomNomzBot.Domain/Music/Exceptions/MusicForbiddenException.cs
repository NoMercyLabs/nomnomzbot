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

using NomNomzBot.Domain.Platform.Exceptions;

namespace NomNomzBot.Domain.Music.Exceptions;

/// <summary>
/// A provider transport call was rejected 403 for a reason OTHER than <c>PREMIUM_REQUIRED</c> (Spotify's
/// own <c>SendPlayerCommandAsync</c> intercepts and throws <see cref="PremiumRequiredException"/> for
/// that specific reason before this exception is ever reached). Sibling of
/// <see cref="MusicAuthenticationFailedException"/> (S003): a live 401 means the token itself is dead
/// (<c>needs_reauth</c>); a live non-premium 403 means the token is alive but the account/grant lacks
/// permission for the call (<c>forbidden</c>) — two distinct, honestly-worded reasons instead of one
/// blanket "auth broken". The first Result-typed surface (<c>IMusicService</c>) maps this to
/// <c>Failure("MUSIC_FORBIDDEN")</c>.
/// </summary>
public sealed class MusicForbiddenException : DomainException
{
    public MusicForbiddenException(string provider)
        : base($"{provider} refused that request — check the connection's permissions.")
    {
        Provider = provider;
    }

    /// <summary>The provider registry key whose call was forbidden (e.g. "spotify").</summary>
    public string Provider { get; }
}
