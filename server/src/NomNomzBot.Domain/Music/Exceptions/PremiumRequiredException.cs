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
/// A provider transport call was rejected because the connected account lacks the provider's
/// premium tier (music-sr.md §3.5: Spotify player writes return 403 reason <c>PREMIUM_REQUIRED</c>).
/// The §3.5 seam members return plain <c>Task</c>, so this exception is the in-seam carrier; the
/// first Result-typed surface (<c>IMusicService</c>) catches it and maps it to
/// <c>Failure("PREMIUM_REQUIRED")</c> — callers above that surface never see a throw.
/// </summary>
public sealed class PremiumRequiredException : DomainException
{
    public PremiumRequiredException(string provider)
        : base($"{provider} Premium is required for playback control.")
    {
        Provider = provider;
    }

    /// <summary>The provider registry key that rejected the call (e.g. "spotify").</summary>
    public string Provider { get; }
}
