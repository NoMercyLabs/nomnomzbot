// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
