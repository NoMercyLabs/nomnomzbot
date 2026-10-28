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

using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Domain.Platform.Exceptions;

namespace NomNomzBot.Domain.Music.Exceptions;

/// <summary>
/// An engine command (pause, resume, stop, seek, volume) could not run because a precondition is missing.
/// Both engines raise this one type with the same <see cref="EngineBlockReason"/>, so the director treats a
/// dead Spotify token and a closed YouTube player the same way.
/// </summary>
public sealed class PlaybackEngineBlockedException : DomainException
{
    public PlaybackEngineBlockedException(EngineKind engine, EngineBlockReason reason)
        : base($"{engine} playback is blocked: {reason}.")
    {
        Engine = engine;
        Reason = reason;
    }

    public EngineKind Engine { get; }

    public EngineBlockReason Reason { get; }
}
