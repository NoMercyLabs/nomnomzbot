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
/// A provider transport call was rejected because nothing is currently playing on any of the
/// account's devices (Spotify's player writes return 404 reason <c>NO_ACTIVE_DEVICE</c>). The
/// §3.5 seam members return plain <c>Task</c>/<c>Task&lt;bool&gt;</c>, so this exception is the
/// in-seam carrier; the first Result-typed surface (<c>IMusicService</c>) catches it and maps it
/// to <c>Failure("NO_ACTIVE_DEVICE")</c> — a viewer/streamer sees a distinct "start playback and
/// try again" reply rather than a false success or a swallowed failure.
/// </summary>
public sealed class NoActiveDeviceException : DomainException
{
    public NoActiveDeviceException(string provider)
        : base($"{provider} has no active playback device.")
    {
        Provider = provider;
    }

    /// <summary>The provider registry key that rejected the call (e.g. "spotify").</summary>
    public string Provider { get; }
}
