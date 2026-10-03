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

/// <summary>The broadcaster (or a moderator) activated Shield Mode (<c>channel.shield_mode.begin</c>).</summary>
public sealed class ShieldModeBeganEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the moderator or broadcaster who turned Shield Mode on (a number as text).</summary>
    public required string ModeratorId { get; init; }

    /// <summary>Their display name, as shown in chat.</summary>
    public required string ModeratorDisplayName { get; init; }

    /// <summary>When Shield Mode began, in UTC time.</summary>
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>The broadcaster (or a moderator) deactivated Shield Mode (<c>channel.shield_mode.end</c>).</summary>
public sealed class ShieldModeEndedEvent : DomainEventBase
{
    /// <summary>The Twitch user id of the moderator or broadcaster who turned Shield Mode off (a number as text).</summary>
    public required string ModeratorId { get; init; }

    /// <summary>Their display name, as shown in chat.</summary>
    public required string ModeratorDisplayName { get; init; }

    /// <summary>When Shield Mode ended, in UTC time.</summary>
    public required DateTimeOffset EndedAt { get; init; }
}
