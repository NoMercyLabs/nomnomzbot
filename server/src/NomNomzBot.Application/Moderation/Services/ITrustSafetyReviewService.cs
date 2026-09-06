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
using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The platform-wide trust & safety desk (S-ADMIN-8a): correlate one actor's spam-defence detections
/// ACROSS every tenant of this deployment, and review the account actions the spam-defence engine took
/// automatically (<c>SpamOutcome.DeleteAndEscalate</c>, dry run off). Both operations cross the tenant
/// boundary, so every entry point gates and audits FIRST through <c>IPlatformIamService.AuthorizePlatformAsync</c>,
/// naming the acting operator and the detection/actor touched — the same funnel <c>IAdminSupportService</c>
/// uses. Requires <c>trust-safety:review</c>.
/// </summary>
public interface ITrustSafetyReviewService
{
    /// <summary>
    /// Every actor whose detections were recorded in two or more tenants, each with the real detections
    /// that back the correlation. Computed from persisted <see cref="Domain.Moderation.Entities.SpamDetection"/>
    /// rows only — never a heuristic that merely looks plausible.
    /// </summary>
    Task<Result<IReadOnlyList<CrossTenantAbuseSignalDto>>> GetCrossTenantSignalsAsync(
        Guid actingPrincipalId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>
    /// The queue of automatic account actions awaiting review, newest first, each carrying the evidence
    /// that caused it and a preview of what an overturn would reverse.
    /// </summary>
    Task<Result<PagedList<TrustSafetyReviewItemDto>>> GetReviewQueueAsync(
        Guid actingPrincipalId,
        string justification,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// The operator agrees with an automatic action: closes the review without touching the action
    /// itself. Fails if the detection is not a pending automatic-action row, or is already overturned.
    /// </summary>
    Task<Result> ConfirmAsync(
        Guid actingPrincipalId,
        Guid detectionId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>
    /// The operator disagrees: REVERSES the real account action (removes the Twitch timeout/ban the
    /// engine issued) before marking the detection overturned. The row is only stamped overturned when
    /// the reversal itself succeeds — a failed reversal leaves the queue item exactly as it was, so the
    /// state shown is never a claim the platform cannot back up.
    /// </summary>
    Task<Result> OverturnAsync(
        Guid actingPrincipalId,
        Guid detectionId,
        string justification,
        CancellationToken ct = default
    );
}
