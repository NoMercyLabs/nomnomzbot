// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Gdpr;

namespace NomNomzBot.Application.Services;

/// <summary>
/// The GDPR erasure / export / opt-out orchestrator (gdpr-crypto.md §3.7) — supersedes the legacy
/// <c>IGdprService</c>. Drives the self-service my-data plane (<c>GdprController</c>) and the operator
/// compliance plane (<c>ComplianceController</c>). Every request is recorded as an <c>ErasureRequest</c> row
/// and audited append-only in <c>ComplianceAuditLog</c>.
/// <para>
/// Two-phase write semantics: the <c>ErasureRequest</c> row is persisted in its own save BEFORE the pipeline
/// transaction begins, so the request record survives a mid-pipeline rollback; the destructive pipeline runs
/// in ONE <c>IUnitOfWork</c> transaction; a failure rolls that back and stamps <c>Status=failed</c> +
/// the failure audit row in a third, separate save.
/// </para>
/// </summary>
public interface IErasureService
{
    /// <summary>
    /// Executes the full erasure pipeline for the subject in one transaction: profile anonymization,
    /// chat/records/viewer-data scrub (cross-channel, ignoring soft-delete), vaulted OAuth token revocation,
    /// auth-session/refresh-token revocation + residual IP scrub, consent withdrawal, and O(1) crypto-shred
    /// of the subject's DEK(s). Completes the request with an <c>AnonymizationReport</c> and a
    /// <c>completed</c> audit row; on any step failure the transaction rolls back and the surviving request
    /// row is marked <c>failed</c> (audit <c>Outcome=failed</c>). Idempotent on already-anonymized subjects.
    /// </summary>
    Task<Result<ErasureRequestDto>> RequestErasureAsync(
        RequestErasureRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Produces the machine-readable JSON export document (Newtonsoft-serialized) of everything held about
    /// the subject — profile, chat, records, connected services, vaulted OAuth connections (never token
    /// ciphertext), and consents. Read-only w.r.t. subject data; records the request + an <c>export</c>
    /// audit row.
    /// </summary>
    Task<Result<DataExportDto>> RequestExportAsync(
        RequestExportRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Legitimate-interest processing opt-out (the lighter kind): withdraws the subject's
    /// <c>marketing</c> / <c>leaderboard_opt_in</c> consents and flags analytics processing opt-out on the
    /// subject's viewer profiles. No key destruction, no data scrub; audited as <c>consent_change</c>.
    /// </summary>
    Task<Result<ErasureRequestDto>> RequestOptOutAsync(
        RequestOptOutRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Reads one request. Callers own subject-scoping (the self-service plane hides foreign requests).</summary>
    Task<Result<ErasureRequestDto>> GetRequestAsync(
        Guid erasureRequestId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Pages requests, newest first. <paramref name="subjectUserId"/> scopes to one subject (the self-service
    /// plane always passes the caller); null lists all subjects (the audited compliance plane).
    /// </summary>
    Task<Result<PagedList<ErasureRequestDto>>> ListRequestsAsync(
        PaginationParams pagination,
        Guid? subjectUserId,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );
}
