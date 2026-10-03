// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published when a viewer cheers with bits (Twitch EventSub channel.cheer) or the platform-equivalent
/// paid on-platform currency — e.g. Kick's Kicks (webhook kicks.gifted, <see cref="Bits"/> = amount);
/// <see cref="Provider"/> names the source (supporter-events.md §4.1).
/// </summary>
public sealed class CheerEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this cheer was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The id of the viewer who cheered (a number as text on Twitch). Empty when the cheer is anonymous.</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's display name, as shown in chat. Empty when the cheer is anonymous.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>How many Bits (or the equivalent amount on another platform) were cheered.</summary>
    public required int Bits { get; init; }

    /// <summary>The chat message sent with the cheer. Empty when there is no message.</summary>
    public required string Message { get; init; }

    /// <summary>True when the viewer cheered anonymously.</summary>
    public required bool IsAnonymous { get; init; }

    /// <summary>
    /// The alert's decorative image, when the source event has one — YouTube Super Stickers only
    /// (resolved from the sticker id via <c>IYouTubeSuperStickerImageResolver</c>, supporter-events.md §4.1).
    /// Twitch/Kick cheers never set this: bits/Kicks carry no image concept.
    /// </summary>
    public string? ImageUrl { get; init; }
}
