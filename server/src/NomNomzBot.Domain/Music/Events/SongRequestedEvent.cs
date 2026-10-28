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

/// <summary>When a viewer requests a song.</summary>
public sealed class SongRequestedEvent : DomainEventBase
{
    /// <summary>The id of the viewer who requested the song. Set to anonymous when the requester is not known.</summary>
    public required string UserId { get; init; }

    /// <summary>The name of the viewer who requested the song, as shown in chat. Set to anonymous when the requester is not known.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The platform user id of the viewer who requested the song. Null when no viewer is behind the request.</summary>
    public string? RequesterUserId { get; init; }

    /// <summary>The id of the requested track at the music service.</summary>
    public required string TrackUri { get; init; }

    /// <summary>The title of the requested track.</summary>
    public required string TrackName { get; init; }
}
