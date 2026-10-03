// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Music.Events;

/// <summary>When the current song is skipped.</summary>
public sealed class SongSkippedEvent : DomainEventBase
{
    /// <summary>The Twitch user id (a number as text) of the person who skipped the song.</summary>
    public required string SkippedByUserId { get; init; }

    /// <summary>The title of the skipped track.</summary>
    public required string TrackName { get; init; }
}
