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
/// A provider transport call could not obtain a usable access token for the channel's connection
/// (dead/revoked/expired refresh token). The §3.5 seam members return plain <c>Task</c>/
/// <c>Task&lt;bool&gt;</c>, so this exception is the in-seam carrier; the first Result-typed
/// surface (<c>IMusicService</c>) catches it and maps it to <c>Failure("MUSIC_AUTH_FAILED")</c> —
/// distinct from <c>PREMIUM_REQUIRED</c> and <c>NO_ACTIVE_DEVICE</c> so a streamer knows the
/// connection itself needs re-authorizing, not just a playback nudge.
/// </summary>
public sealed class MusicAuthenticationFailedException : DomainException
{
    public MusicAuthenticationFailedException(string provider)
        : base($"{provider} connection needs to be reconnected.")
    {
        Provider = provider;
    }

    /// <summary>The provider registry key whose token could not be used (e.g. "spotify").</summary>
    public string Provider { get; }
}
