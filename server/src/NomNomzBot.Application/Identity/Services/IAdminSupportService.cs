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
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// The cross-tenant support desk (S-ADMIN-7a) — find one PERSON anywhere on the platform and read the state
/// the product actually holds for them, each fact labeled with the tenant it belongs to. A read-only sibling
/// of <see cref="IPlatformAdminService"/>: it never mutates and it never acts AS the subject (that is
/// <c>user:impersonate</c>, deliberately a different key). Both methods take a mandatory justification and
/// gate-and-audit BEFORE any subject data is read, naming the acting operator and the subject on the row.
/// </summary>
public interface IAdminSupportService
{
    /// <summary>
    /// Finds people by username / display name / platform id across EVERY tenant — the tenant query filter is
    /// deliberately bypassed, which is exactly what makes this privileged. Requires <c>user:support:view</c>;
    /// justification is mandatory and lands on the audit row with the search term as the target resource.
    /// </summary>
    Task<Result<PagedList<SupportPersonSearchResultDto>>> SearchPeopleAsync(
        Guid actingPrincipalId,
        string search,
        string justification,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// ONE view of one person's real state across every tenant: IAM roles, community standing, moderation
    /// standing + history, trust/heat, entitlements and platform connections. A datum the system does not hold
    /// is OMITTED, never zero-filled. Requires <c>user:support:view</c>; justification is mandatory and the
    /// subject user id lands on the audit row.
    /// </summary>
    Task<Result<SupportPersonViewDto>> GetPersonAsync(
        Guid actingPrincipalId,
        Guid subjectUserId,
        string justification,
        CancellationToken ct = default
    );

    /// <summary>
    /// Replays what actually happened to this person — the real rows recorded for them in the append-only
    /// event journal, across EVERY tenant, newest first, each labeled with the tenant it happened in (or
    /// <c>null</c> for a platform-global event). A person with no recorded history gets an empty page, never
    /// a failure. Requires <c>user:support:view</c>; justification is mandatory and the subject user id lands
    /// on the audit row, same as <see cref="GetPersonAsync"/>.
    /// </summary>
    Task<Result<PagedList<SupportPersonHistoryEntryDto>>> GetPersonHistoryAsync(
        Guid actingPrincipalId,
        Guid subjectUserId,
        string justification,
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
