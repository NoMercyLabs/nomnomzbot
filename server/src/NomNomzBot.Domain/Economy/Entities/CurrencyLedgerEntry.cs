// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// One immutable movement of a viewer's balance (economy.md K.3) — the append-only source of truth the
/// <c>CurrencyAccount.Balance</c> projection is folded from. <c>TenantPosition</c> is the per-tenant monotonic
/// sequence (gap-free order); <c>Amount</c> is signed (credit &gt; 0, debit &lt; 0); <c>BalanceAfter</c> snapshots
/// the post-entry balance. APPEND-ONLY: no <c>UpdatedAt</c>/<c>DeletedAt</c>; tenant-scoped (the filter applies),
/// keyed by a <c>long</c> identity. Corrections are new reversing entries, never edits.
/// </summary>
public class CurrencyLedgerEntry : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }
    public long TenantPosition { get; set; }
    public Guid AccountId { get; set; }
    public Guid ViewerUserId { get; set; }
    public string ViewerTwitchUserId { get; set; } = null!;
    public long Amount { get; set; }
    public long BalanceAfter { get; set; }
    public CurrencyEntryType EntryType { get; set; }
    public CurrencyLedgerSourceType? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public long? RelatedEntryId { get; set; }
    public Guid? EventId { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
