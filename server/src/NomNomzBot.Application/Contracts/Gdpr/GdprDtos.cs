// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Gdpr;

// GDPR use-case contracts (gdpr-crypto.md §4.2/§4.3). Enum-like fields are validated strings matching the
// schema [VC:enum] sets; validation lives in the services, so an invalid value fails as VALIDATION_FAILED,
// never as a silent default.

/// <summary>
/// Grants (upserts) the single active consent row for (BroadcasterId, SubjectUserId, ConsentType).
/// <c>ProofOfConsentIp</c> is accepted for forward-compatibility but deliberately NOT persisted today —
/// <c>ConsentRecord.IpAddressCipher</c> is unused by design (the entity's own doc-comment governs;
/// proportionate privacy over speculative sealing).
/// </summary>
public sealed record GrantConsentRequest(
    Guid SubjectUserId,
    Guid? BroadcasterId,
    string ConsentType,
    string LawfulBasis,
    string? ConsentVersion,
    string? Source,
    string? ProofOfConsentIp
);

public sealed record RequestErasureRequest(
    Guid SubjectUserId,
    Guid? BroadcasterId,
    string RequestedBy,
    string Scope
);

public sealed record RequestExportRequest(
    Guid SubjectUserId,
    Guid? BroadcasterId,
    string RequestedBy
);

public sealed record RequestOptOutRequest(
    Guid SubjectUserId,
    Guid? BroadcasterId,
    string RequestedBy
);

public sealed record ConsentRecordDto(
    Guid Id,
    Guid? BroadcasterId,
    Guid SubjectUserId,
    string ConsentType,
    string Status,
    string LawfulBasis,
    string? ConsentVersion,
    DateTime GrantedAt,
    DateTime? WithdrawnAt,
    DateTime? ExpiresAt
);

public sealed record ErasureRequestDto(
    Guid Id,
    Guid SubjectUserId,
    string SubjectIdHash,
    Guid? BroadcasterId,
    string RequestType,
    string RequestedBy,
    string Status,
    string Scope,
    bool CryptoShredApplied,
    bool AnonymizationApplied,
    int RowsAffected,
    string? ExportLocation,
    string? ExportFormat,
    string? FailureReason,
    DateTime RequestedAt,
    DateTime? CompletedAt
);

/// <summary>
/// The machine-readable export result. <c>Document</c> carries the full Newtonsoft-serialized JSON export
/// document inline (as-built extension of the spec shape — the self-service endpoint hands the document
/// straight to the caller; there is no file store behind <c>ExportLocation</c>, which is the API path that
/// identifies the request).
/// </summary>
public sealed record DataExportDto(
    Guid ErasureRequestId,
    string ExportFormat,
    string ExportLocation,
    long SizeBytes,
    int RowsAffected,
    DateTime GeneratedAt,
    string Document
);

/// <summary>Internal per-erasure report shape (persisted as <c>ErasureRequest.ReportJson</c>).</summary>
public sealed record AnonymizationReport(
    int TablesAffectedCount,
    int RowsAffected,
    IReadOnlyList<string> TablesAffected
);
