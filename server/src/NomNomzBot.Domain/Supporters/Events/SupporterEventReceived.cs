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

namespace NomNomzBot.Domain.Supporters.Events;

// DomainEventBase is a class, so this is a sealed CLASS (records may not inherit a non-record class).
// BroadcasterId (the tenant) is inherited from DomainEventBase. Namespaced module-first per repo convention
// (a deliberate delta from supporter-events.md §2's `Domain.Events`, matching the engagement precedent).

/// <summary>
/// A normalized monetization event was ingested (supporter-events.md §2). Consumed by the trigger source
/// (fires <c>supporter.&lt;kind&gt;</c> + <c>supporter.any</c> bound responses), the Alerts widget, and the
/// opt-in economy reward. Carries the display fields for templating plus the persisted row id so a consumer
/// can load the full record (merch line-items, raw payload).
/// </summary>
public sealed class SupporterEventReceived : DomainEventBase
{
    /// <summary>The key of the service that sent the event.</summary>
    public required string SourceKey { get; init; }

    /// <summary><c>tip</c> / <c>membership</c> / <c>merch</c> / <c>charity</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The supporter's name, as shown on the service.</summary>
    public required string SupporterDisplayName { get; init; }

    /// <summary>The supporter's user id in this system. Empty when the supporter is not linked to a user.</summary>
    public Guid? SupporterUserId { get; init; }

    /// <summary>Amount in minor units (cents).</summary>
    public long? AmountMinor { get; init; }

    /// <summary>The currency of the amount, as an upper case three-letter code, as the service sent it. Empty when there is no amount.</summary>
    public string? Currency { get; init; }

    /// <summary>The membership tier name. Empty when the event has no tier.</summary>
    public string? Tier { get; init; }

    /// <summary>How many items were bought. Empty when the event has no quantity.</summary>
    public int? Quantity { get; init; }

    /// <summary>The message the supporter wrote. Empty when there is no message.</summary>
    public string? MessageText { get; init; }

    /// <summary>True when the support repeats. The service marks memberships and recurring donations this way.</summary>
    public bool IsRecurring { get; init; }

    /// <summary>The persisted <c>SupporterEvent</c> row id (full record for line-items etc.).</summary>
    public required Guid SupporterEventId { get; init; }
}
