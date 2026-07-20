// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Plane-C privileged tenant operations (stream-admin.md §5 platform rows) — suspend/reinstate tenants,
/// audited support access, and the Plane-C audit search. Each action carries the IAM policy (entry gate,
/// audited); the service re-asserts the same key per call with the tenant-targeted audit row. Extends the
/// read-only <c>AdminController</c> surface under the same <c>admin</c> route.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[Authorize]
[Tags("Admin")]
public class PlatformAdminController(
    IPlatformAdminService admin,
    IPlatformIamService iam,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>Paged tenant listing with search/status/live filters.</summary>
    [HttpGet("tenants")]
    [Authorize(Policy = IamPermissionKeys.TenantRead)]
    [ProducesResponseType<PaginatedResponse<AdminTenantDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTenants(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] bool? isLive,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminTenantDto>> result = await admin.ListTenantsAsync(
            await ActingPrincipalIdAsync(ct),
            new AdminTenantQuery(search, status, isLive),
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Tenant detail — status, tier, owner, membership count.</summary>
    [HttpGet("tenants/{broadcasterId:guid}")]
    [Authorize(Policy = IamPermissionKeys.TenantRead)]
    [ProducesResponseType<StatusResponseDto<AdminTenantDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenant(Guid broadcasterId, CancellationToken ct) =>
        ResultResponse(
            await admin.GetTenantAsync(await ActingPrincipalIdAsync(ct), broadcasterId, ct)
        );

    /// <summary>Suspends a tenant (<c>suspended</c> | <c>platform_banned</c>) — enforced by the bot lifecycle and tenant resolution.</summary>
    [HttpPost("tenants/{broadcasterId:guid}/suspend")]
    [Authorize(Policy = IamPermissionKeys.TenantSuspend)]
    public async Task<IActionResult> SuspendTenant(
        Guid broadcasterId,
        [FromBody] SuspendTenantRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await admin.SuspendTenantAsync(
                await ActingPrincipalIdAsync(ct),
                broadcasterId,
                request,
                ct
            )
        );

    /// <summary>Reinstates a suspended tenant to <c>active</c>.</summary>
    [HttpPost("tenants/{broadcasterId:guid}/reinstate")]
    [Authorize(Policy = IamPermissionKeys.TenantSuspend)]
    public async Task<IActionResult> ReinstateTenant(
        Guid broadcasterId,
        [FromBody] ReinstateTenantRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await admin.ReinstateTenantAsync(
                await ActingPrincipalIdAsync(ct),
                broadcasterId,
                request.Justification,
                ct
            )
        );

    /// <summary>Begins audited support access to one tenant (time-boxed, tenant-narrowed role assignment).</summary>
    [HttpPost("tenants/{broadcasterId:guid}/access")]
    [Authorize(Policy = IamPermissionKeys.TenantAccess)]
    [ProducesResponseType<StatusResponseDto<TenantAccessGrantDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BeginTenantAccess(
        Guid broadcasterId,
        [FromBody] BeginTenantAccessRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await admin.BeginTenantAccessAsync(
                await ActingPrincipalIdAsync(ct),
                broadcasterId,
                request,
                ct
            )
        );

    /// <summary>Ends a support-access grant (revokes the assignment).</summary>
    [HttpDelete("access/{accessGrantId:guid}")]
    [Authorize(Policy = IamPermissionKeys.TenantAccess)]
    public async Task<IActionResult> EndTenantAccess(Guid accessGrantId, CancellationToken ct) =>
        ResultResponse(
            await admin.EndTenantAccessAsync(await ActingPrincipalIdAsync(ct), accessGrantId, ct)
        );

    /// <summary>
    /// Begins an act-as impersonation of a registered user — mints an access-only token carrying that
    /// user's identity and roles (never the operator's). Justification is mandatory and audited.
    /// </summary>
    [HttpPost("users/{userId:guid}/impersonate")]
    [Authorize(Policy = IamPermissionKeys.UserImpersonate)]
    [ProducesResponseType<StatusResponseDto<ImpersonationTokenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Impersonate(
        Guid userId,
        [FromBody] ImpersonateUserRequest req,
        CancellationToken ct
    ) =>
        ResultResponse(
            await admin.StartImpersonationAsync(
                await ActingPrincipalIdAsync(ct),
                userId,
                req.Justification,
                ct
            )
        );

    /// <summary>Paged Plane-C audit search by principal/tenant/permission/outcome/time.</summary>
    [HttpGet("audit")]
    [Authorize(Policy = IamPermissionKeys.AuditRead)]
    [ProducesResponseType<PaginatedResponse<IamAuditEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAudit(
        [FromQuery] Guid? principalId,
        [FromQuery] Guid? targetBroadcasterId,
        [FromQuery] string? permission,
        [FromQuery] string? outcome,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<IamAuditEntryDto>> result = await admin.SearchAuditAsync(
            await ActingPrincipalIdAsync(ct),
            new AuditSearchQuery(principalId, targetBroadcasterId, permission, outcome, from, to),
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>
    /// The caller's IAM principal id for the service's audited re-check. Self-host (zero principals) has no
    /// principal row and the service short-circuits to allow — <c>Guid.Empty</c> is correct there.
    /// </summary>
    private async Task<Guid> ActingPrincipalIdAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(currentUser.UserId, out Guid userId))
            return Guid.Empty;

        Result<IamPrincipalDto?> principal = await iam.ResolvePrincipalAsync(userId, ct);
        return principal is { IsSuccess: true, Value: not null } ? principal.Value.Id : Guid.Empty;
    }
}

/// <summary>Body for the reinstate action — the justification lands in the audit row.</summary>
public sealed record ReinstateTenantRequest(string Justification);
