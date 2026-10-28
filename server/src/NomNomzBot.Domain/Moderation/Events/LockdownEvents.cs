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

using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// A lockdown window opened and tightened the room (spam-defense.md §L5.1). Lists controls, never people:
/// lockdown never acts on a person.
/// </summary>
public sealed class LockdownEngagedEvent : DomainEventBase
{
    /// <summary>The lockdown window this event is about.</summary>
    public required Guid WindowId { get; init; }

    /// <summary>The platform whose room was tightened (<c>twitch</c>, <c>kick</c>, ...).</summary>
    public required string Platform { get; init; }

    /// <summary>Why, in words an operator can read back.</summary>
    public required string Trigger { get; init; }

    /// <summary>When the window opened.</summary>
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
    /// <summary>The lockdown window this event is about.</summary>
    public required Guid WindowId { get; init; }

    /// <summary>The platform whose room the window covers (<c>twitch</c>, <c>kick</c>, ...).</summary>
    public required string Platform { get; init; }

    /// <summary>The new end time.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Every control a lockdown window tightened was confirmed put back.</summary>
public sealed class LockdownRestoredEvent : DomainEventBase
{
    /// <summary>The lockdown window this event is about.</summary>
    public required Guid WindowId { get; init; }

    /// <summary>The platform whose room the window covers (<c>twitch</c>, <c>kick</c>, ...).</summary>
    public required string Platform { get; init; }

    /// <summary>When the last control was confirmed put back.</summary>
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
    /// <summary>The lockdown window this event is about.</summary>
    public required Guid WindowId { get; init; }

    /// <summary>The platform whose room the window covers (<c>twitch</c>, <c>kick</c>, ...).</summary>
    public required string Platform { get; init; }

    /// <summary>The controls still tightened.</summary>
    public required IReadOnlyList<LockdownControl> Failed { get; init; }
}
