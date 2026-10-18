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

namespace NomNomzBot.Domain.MediaShare.Events;

/// <summary>
/// The server told the media-share overlay to do something. <see cref="Command"/> is one of
/// load | play | pause | stop | seek | volume. The item fields are set only for <c>load</c>.
/// </summary>
public sealed class MediaPlayerCommandIssuedEvent : DomainEventBase
{
    public required string Command { get; init; }

    public Guid? RequestId { get; init; }

    public string? SourceType { get; init; }

    public string? MediaRef { get; init; }

    public string? SourceUrl { get; init; }

    public string? Title { get; init; }

    public int? DurationSeconds { get; init; }

    public int? PositionSeconds { get; init; }

    public int? Volume { get; init; }
}

/// <summary>
/// What the media-share player is doing now, as the overlay reported it. <see cref="State"/> is one of
/// idle | playing | paused | ended | error.
/// </summary>
public sealed class MediaPlayerStateChangedEvent : DomainEventBase
{
    public required string State { get; init; }

    public Guid? RequestId { get; init; }

    public string? Title { get; init; }

    public string? SharerName { get; init; }

    public string? ThumbnailUrl { get; init; }

    public int PositionSeconds { get; init; }

    public int DurationSeconds { get; init; }

    public int Volume { get; init; }
}
