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

/// <summary>
/// The request at the head of the queue cannot start because its engine has nothing to play on (a YouTube
/// request with no Music Player page open). The queue plays the requests behind it and keeps this one, so it
/// plays once the page opens. Published once per request, so the wait is announced instead of silent.
/// </summary>
public sealed class SongRequestPassedOverEvent : DomainEventBase
{
    /// <summary>The id of the track at the music service.</summary>
    public required string TrackUri { get; init; }

    /// <summary>The title of the passed-over request.</summary>
    public required string TrackName { get; init; }
}
