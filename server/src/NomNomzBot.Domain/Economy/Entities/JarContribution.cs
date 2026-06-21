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
    public long? LedgerEntryId { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
