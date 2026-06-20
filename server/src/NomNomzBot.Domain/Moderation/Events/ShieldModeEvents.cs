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
    public required string ModeratorId { get; init; }
    public required string ModeratorDisplayName { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>The broadcaster (or a moderator) deactivated Shield Mode (<c>channel.shield_mode.end</c>).</summary>
public sealed class ShieldModeEndedEvent : DomainEventBase
{
    public required string ModeratorId { get; init; }
    public required string ModeratorDisplayName { get; init; }
    public required DateTimeOffset EndedAt { get; init; }
}
