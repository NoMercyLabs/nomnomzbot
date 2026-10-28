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
