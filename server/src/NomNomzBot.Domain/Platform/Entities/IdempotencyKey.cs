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

namespace NomNomzBot.Domain.Platform.Entities;

/// <summary>
/// A scoped idempotency marker (schema §O.4): records that a unit of work for <c>(Scope, Key, BroadcasterId)</c>
/// already ran, so a redelivery is short-circuited. The EventSub dispatcher writes one per inbound notification
/// (<c>Scope="eventsub"</c>, <c>Key=</c> Twitch message-id). Append-only fact: <c>bigint</c> identity PK, no
/// soft-delete; expired rows are pruned by retention, not user-deleted.
/// </summary>
public class IdempotencyKey
{
    /// <summary>Global insert order. Database identity (<c>bigint</c>).</summary>
    public long Id { get; set; }

    /// <summary>The action namespace, e.g. <c>eventsub</c>.</summary>
    public string Scope { get; set; } = null!;

    /// <summary>The unique key within the scope, e.g. the Twitch message-id.</summary>
    public string Key { get; set; } = null!;

    /// <summary>Owning tenant; <c>null</c> = platform-global.</summary>
    public Guid? BroadcasterId { get; set; }

    /// <summary>Optional hash of the produced result (for replay-with-same-result verification).</summary>
    public string? ResultHash { get; set; }

    /// <summary>When this marker may be pruned by retention.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When this marker was first recorded.</summary>
    public DateTime CreatedAt { get; set; }
}
