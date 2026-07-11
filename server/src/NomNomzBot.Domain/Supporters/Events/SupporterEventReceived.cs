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
    public required string SourceKey { get; init; }

    /// <summary><c>tip</c> / <c>membership</c> / <c>merch</c> / <c>charity</c>.</summary>
    public required string Kind { get; init; }

    public required string SupporterDisplayName { get; init; }

    public Guid? SupporterUserId { get; init; }

    /// <summary>Amount in minor units (cents).</summary>
    public long? AmountMinor { get; init; }

    public string? Currency { get; init; }

    public string? Tier { get; init; }

    public int? Quantity { get; init; }

    public string? MessageText { get; init; }

    public bool IsRecurring { get; init; }

    /// <summary>The persisted <c>SupporterEvent</c> row id (full record for line-items etc.).</summary>
    public required Guid SupporterEventId { get; init; }
}
