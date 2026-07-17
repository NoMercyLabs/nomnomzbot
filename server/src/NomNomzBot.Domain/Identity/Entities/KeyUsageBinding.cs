// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// Inventory row (schema Q.2) recording that a table/column stores ciphertext sealed under a DEK
/// (<see cref="CryptoKey"/>). Written idempotently on every <c>ISubjectKeyService.ProtectAsync</c>, unique per
/// <c>(CryptoKeyId, ResourceTable, ResourceColumn)</c>. Feeds shred-impact reporting
/// (<see cref="ComplianceAuditLog.KeysShredded"/>) and the rotation planner (which resources a successor DEK
/// re-covers). Append-only in spirit: rows are never mutated, only asserted; no soft delete, no timestamps
/// (per the locked Q.2 field list).
/// </summary>
public class KeyUsageBinding
{
    /// <summary>Append-only surrogate (bigint identity per schema Q.2).</summary>
    public long Id { get; set; }

    /// <summary>The DEK the resource is sealed under (FK → <see cref="CryptoKey"/>).</summary>
    public Guid CryptoKeyId { get; set; }

    /// <summary>The protected resource's table (a DbSet name, or a logical <c>envelope:*</c> store).</summary>
    [MaxLength(100)]
    public string ResourceTable { get; set; } = null!;

    /// <summary>The protected column (or the field role for a logical envelope store).</summary>
    [MaxLength(100)]
    public string ResourceColumn { get; set; } = null!;

    /// <summary>Owning tenant when the sealed resource is tenant-scoped; null for subject/platform data.</summary>
    public Guid? BroadcasterId { get; set; }
}
