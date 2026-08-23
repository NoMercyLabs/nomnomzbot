// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
