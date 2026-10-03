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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>A user was granted the VIP role on the channel (<c>channel.vip.add</c>).</summary>
public sealed class VipAddedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the new VIP (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The new VIP's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The new VIP's login name (lowercase).</summary>
    public required string UserLogin { get; init; }
}

/// <summary>A user's VIP role was revoked on the channel (<c>channel.vip.remove</c>).</summary>
public sealed class VipRemovedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the user who lost the VIP role (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The user's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The user's login name (lowercase).</summary>
    public required string UserLogin { get; init; }
}
