// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// A lockdown window opened and tightened the room (spam-defense.md §L5.1). Lists controls, never people:
/// lockdown never acts on a person.
/// </summary>
public sealed class LockdownEngagedEvent : DomainEventBase
{
    public required Guid WindowId { get; init; }

    /// <summary>The platform whose room was tightened (<c>twitch</c>, <c>kick</c>, ...).</summary>
    public required string Platform { get; init; }

    /// <summary>Why, in words an operator can read back.</summary>
    public required string Trigger { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the window ends by itself.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Controls this window tightened.</summary>
    public required IReadOnlyList<LockdownControl> Engaged { get; init; }

    /// <summary>Requested controls this platform does not offer here.</summary>
    public required IReadOnlyList<LockdownControl> Unavailable { get; init; }

    /// <summary>Controls the platform refused: NOT in force.</summary>
    public required IReadOnlyList<LockdownControl> ApplyFailed { get; init; }
}

/// <summary>An active lockdown window was pushed out because a second engage arrived inside the policy ceiling.</summary>
public sealed class LockdownExtendedEvent : DomainEventBase
{
    public required Guid WindowId { get; init; }

    public required string Platform { get; init; }

    /// <summary>The new end time.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Every control a lockdown window tightened was confirmed put back.</summary>
public sealed class LockdownRestoredEvent : DomainEventBase
{
    public required Guid WindowId { get; init; }

    public required string Platform { get; init; }

    public required DateTimeOffset RestoredAt { get; init; }

    /// <summary>Controls this restore put back (only the ones this attempt touched).</summary>
    public required IReadOnlyList<LockdownControl> Restored { get; init; }
}

/// <summary>
/// A restore left one or more controls tightened. The room is still locked down for them until a later
/// attempt succeeds, so the operator must see it.
/// </summary>
public sealed class LockdownRestoreFailedEvent : DomainEventBase
{
    public required Guid WindowId { get; init; }

    public required string Platform { get; init; }

    /// <summary>The controls still tightened.</summary>
    public required IReadOnlyList<LockdownControl> Failed { get; init; }
}
