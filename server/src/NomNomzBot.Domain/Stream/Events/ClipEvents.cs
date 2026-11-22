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

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>
/// Published once for each clip that is created on a live channel while the bot watches it. Twitch has no EventSub
/// event for a created clip, so the clip detector finds it by reading the channel's clips.
/// </summary>
public sealed class ClipCreatedEvent : DomainEventBase
{
    /// <summary>The Twitch clip id.</summary>
    public required string ClipId { get; init; }

    /// <summary>The clip title.</summary>
    public required string Title { get; init; }

    /// <summary>The display name of the person who made the clip.</summary>
    public required string CreatorName { get; init; }

    /// <summary>The link to the clip.</summary>
    public required string Url { get; init; }

    /// <summary>When Twitch created the clip.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
