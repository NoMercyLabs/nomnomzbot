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

using NomNomzBot.Domain.Economy.Enums;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// An immutable audited movement into or out of a savings jar (economy.md K.6) — the federation audit trail.
/// Links the channel ledger entry that funded/received it. APPEND-ONLY: no <c>UpdatedAt</c>/<c>DeletedAt</c>,
/// keyed by a <c>long</c> identity. CROSS-TENANT (audited per <c>SourceBroadcasterId</c>).
/// </summary>
public class JarContribution
{
    public long Id { get; set; }
    public Guid JarId { get; set; }
    public Guid SourceBroadcasterId { get; set; }
    public Guid? ContributorAccountId { get; set; }
    public Guid? ContributorUserId { get; set; }
    public long Amount { get; set; }
    public JarMovementType MovementType { get; set; }

    /// <summary>The jar balance immediately after this movement — snapshotted so the history reads exactly.</summary>
    public long JarBalanceAfter { get; set; }
    public long? LedgerEntryId { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
