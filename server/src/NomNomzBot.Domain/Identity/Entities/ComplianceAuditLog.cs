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
/// Append-only compliance audit for erasure / export / consent changes (gdpr-crypto.md O.10) — supersedes
/// the legacy <c>DeletionAuditLog</c> (which stays in place for its historical rows). Retains only the
/// <b>hashed</b> subject id, never reversible PII, so the audit trail itself survives the erasure it records.
/// Rows are never updated or deleted (bigint identity PK, no soft delete).
/// </summary>
public class ComplianceAuditLog
{
    public long Id { get; set; }

    /// <summary><c>erasure</c> | <c>export</c> | <c>consent_change</c> (schema [VC:enum]).</summary>
    [MaxLength(20)]
    public string RequestType { get; set; } = null!;

    /// <summary>The originating request; null for standalone consent changes.</summary>
    public Guid? ErasureRequestId { get; set; }

    [MaxLength(64)]
    public string SubjectIdHash { get; set; } = null!;

    public Guid? BroadcasterId { get; set; }

    /// <summary><c>self_service</c> | <c>broadcaster</c> | <c>platform_iam</c> | <c>system</c>.</summary>
    [MaxLength(20)]
    public string RequestedBy { get; set; } = null!;

    /// <summary>The tables the operation touched ([VC:JSON] list).</summary>
    public List<string> TablesAffected { get; set; } = [];

    public int RowsAffected { get; set; }

    public int KeysShredded { get; set; }

    /// <summary><c>completed</c> | <c>partial</c> | <c>failed</c>.</summary>
    [MaxLength(20)]
    public string Outcome { get; set; } = null!;

    public DateTime CompletedAt { get; set; }

    // Stamped by the writing service via the injected TimeProvider (single clock,
    // platform-conventions §3.11) — entities do not self-stamp time.
    public DateTime CreatedAt { get; set; }
}
