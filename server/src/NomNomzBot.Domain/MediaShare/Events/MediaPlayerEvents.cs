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

namespace NomNomzBot.Domain.MediaShare.Events;

/// <summary>
/// The server told the media-share overlay to do something. <see cref="Command"/> is one of
/// load | play | pause | stop | seek | volume. The item fields are set only for <c>load</c>.
/// </summary>
public sealed class MediaPlayerCommandIssuedEvent : DomainEventBase
{
    /// <summary>The command for the player: load, play, pause, stop, seek or volume.</summary>
    public required string Command { get; init; }

    /// <summary>The id of the media-share request the command is about.</summary>
    public Guid? RequestId { get; init; }

    /// <summary>Where the media comes from, as text. Set for load only.</summary>
    public string? SourceType { get; init; }

    /// <summary>The id of the media at its source. Set for load only.</summary>
    public string? MediaRef { get; init; }

    /// <summary>The address the player opens. Set for load only.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>The title of the media. Set for load only.</summary>
    public string? Title { get; init; }

    /// <summary>The length of the media in seconds. Set for load only.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>The position to seek to, in seconds. Set for seek only.</summary>
    public int? PositionSeconds { get; init; }

    /// <summary>The player volume from 0 to 100. Set for volume only.</summary>
    public int? Volume { get; init; }
}

/// <summary>
/// What the media-share player is doing now, as the overlay reported it. <see cref="State"/> is one of
/// idle | playing | paused | ended | error.
/// </summary>
public sealed class MediaPlayerStateChangedEvent : DomainEventBase
{
    /// <summary>What the player is doing: idle, playing, paused, ended or error.</summary>
    public required string State { get; init; }

    /// <summary>The id of the media-share request that is loaded.</summary>
    public Guid? RequestId { get; init; }

    /// <summary>The title of the media that is loaded.</summary>
    public string? Title { get; init; }

    /// <summary>The name of the viewer who shared the media.</summary>
    public string? SharerName { get; init; }

    /// <summary>The address of the thumbnail image.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>The position in the media in seconds.</summary>
    public int PositionSeconds { get; init; }

    /// <summary>The length of the media in seconds.</summary>
    public int DurationSeconds { get; init; }

    /// <summary>The player volume from 0 to 100.</summary>
    public int Volume { get; init; }
}
