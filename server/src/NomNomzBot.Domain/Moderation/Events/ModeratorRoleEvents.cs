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

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>A user was granted the moderator role on the channel (<c>channel.moderator.add</c>).</summary>
public sealed class ModeratorAddedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the new moderator (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The new moderator's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The new moderator's login name (lowercase).</summary>
    public required string UserLogin { get; init; }
}

/// <summary>A user's moderator role was revoked on the channel (<c>channel.moderator.remove</c>).</summary>
public sealed class ModeratorRemovedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the user who lost the moderator role (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The user's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The user's login name (lowercase).</summary>
    public required string UserLogin { get; init; }
}
