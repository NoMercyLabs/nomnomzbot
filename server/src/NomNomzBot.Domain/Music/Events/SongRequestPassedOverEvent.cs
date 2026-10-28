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
