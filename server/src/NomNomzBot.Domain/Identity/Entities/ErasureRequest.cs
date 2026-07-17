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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// Lifecycle record of a GDPR erasure / export / opt-out request (gdpr-crypto.md O.6) — drives the
/// self-service my-data page and links the crypto-shred (<see cref="CryptoKey.ErasureRequestId"/>) back to
/// the request that caused it. Deliberately NOT soft-deletable and NOT tenant-filtered: a subject's request
/// history must survive the erasure itself and be readable across channels.
/// <para>
/// <see cref="SubjectKeyId"/> is nullable as-built (the spec's non-null FK presumes every subject already
/// holds a DEK; a subject who never had one erased still gets a request row). <see cref="ReportJson"/> holds
/// the serialized <c>AnonymizationReport</c> for a completed erasure.
/// </para>
/// </summary>
public class ErasureRequest : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SubjectUserId { get; set; }

    /// <summary>The subject's DEK at request time (FK→CryptoKey); null when the subject held no DEK.</summary>
    public Guid? SubjectKeyId { get; set; }

    /// <summary>Deterministic SHA-256 hex of the subject id — the only subject identifier that survives erasure.</summary>
    [MaxLength(64)]
    public string SubjectIdHash { get; set; } = null!;

    /// <summary>Originating channel context; null for a deployment-wide (platform) request.</summary>
    public Guid? BroadcasterId { get; set; }

    /// <summary><c>erasure</c> | <c>export</c> | <c>opt_out</c> (schema [VC:enum]).</summary>
    [MaxLength(20)]
    public string RequestType { get; set; } = null!;

    /// <summary><c>self_service</c> | <c>broadcaster</c> | <c>platform_iam</c>.</summary>
    [MaxLength(20)]
    public string RequestedBy { get; set; } = null!;

    /// <summary><c>pending</c> | <c>running</c> | <c>completed</c> | <c>failed</c> | <c>cancelled</c>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    /// <summary><c>deployment</c> | <c>instance</c> | <c>channel</c>.</summary>
    [MaxLength(20)]
    public string Scope { get; set; } = null!;

    public bool CryptoShredApplied { get; set; }

    public bool AnonymizationApplied { get; set; }

    [MaxLength(2048)]
    public string? ExportLocation { get; set; }

    [MaxLength(20)]
    public string? ExportFormat { get; set; }

    public int RowsAffected { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Serialized <c>AnonymizationReport</c> (JSON) for a completed erasure; null otherwise.</summary>
    public string? ReportJson { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
