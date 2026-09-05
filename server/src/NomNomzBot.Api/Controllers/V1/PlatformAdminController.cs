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
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
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
/// audited); the service re-asserts the same key per call with the tenant-targeted audit row, resolving the
/// ACTING principal via <see cref="IIamCallerPrincipalResolverService"/> — a resolve failure DENIES the
/// action rather than substituting <c>Guid.Empty</c> (fix D2 item 4). Extends the read-only
/// <c>AdminController</c> surface under the same <c>admin</c> route.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[Authorize]
[Tags("Admin")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class PlatformAdminController(
    IPlatformAdminService admin,
    ICurrentUserService currentUser,
    IIamCallerPrincipalResolverService actingPrincipalResolver
) : BaseController
{
    /// <summary>Paged tenant listing with search/status/live filters.</summary>
    [HttpGet("tenants")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
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
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PagedList<AdminTenantDto>>(null!));

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminTenantDto>> result = await admin.ListTenantsAsync(
            acting.Value,
            new(search, status, isLive),
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Tenant detail — status, tier, owner, membership count.</summary>
    [HttpGet("tenants/{broadcasterId:guid}")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.TenantRead)]
    [ProducesResponseType<StatusResponseDto<AdminTenantDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenant(Guid broadcasterId, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<AdminTenantDetailDto>(null!));
        return ResultResponse(await admin.GetTenantAsync(acting.Value, broadcasterId, ct));
    }

    /// <summary>Suspends a tenant (<c>suspended</c> | <c>platform_banned</c>) — enforced by the bot lifecycle and tenant resolution.</summary>
    [HttpPost("tenants/{broadcasterId:guid}/suspend")]
    [Authorize(Policy = IamPermissionKeys.TenantSuspend)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> SuspendTenant(
        Guid broadcasterId,
        [FromBody] SuspendTenantRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(
            await admin.SuspendTenantAsync(acting.Value, broadcasterId, request, ct)
        );
    }

    /// <summary>Reinstates a suspended tenant to <c>active</c>.</summary>
    [HttpPost("tenants/{broadcasterId:guid}/reinstate")]
    [Authorize(Policy = IamPermissionKeys.TenantSuspend)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> ReinstateTenant(
        Guid broadcasterId,
        [FromBody] ReinstateTenantRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(
            await admin.ReinstateTenantAsync(acting.Value, broadcasterId, request.Justification, ct)
        );
    }

    /// <summary>Begins audited support access to one tenant (time-boxed, tenant-narrowed role assignment).</summary>
    [HttpPost("tenants/{broadcasterId:guid}/access")]
    [Authorize(Policy = IamPermissionKeys.TenantAccess)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<TenantAccessGrantDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BeginTenantAccess(
        Guid broadcasterId,
        [FromBody] BeginTenantAccessRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<TenantAccessGrantDto>(null!));
        return ResultResponse(
            await admin.BeginTenantAccessAsync(acting.Value, broadcasterId, request, ct)
        );
    }

    /// <summary>Ends a support-access grant (revokes the assignment).</summary>
    [NotDestructive(
        "Ends an access grant by stamping its end time; the audited grant row survives for the audit trail."
    )]
    [HttpDelete("access/{accessGrantId:guid}")]
    [Authorize(Policy = IamPermissionKeys.TenantAccess)]
    public async Task<IActionResult> EndTenantAccess(Guid accessGrantId, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(await admin.EndTenantAccessAsync(acting.Value, accessGrantId, ct));
    }

    /// <summary>
    /// Begins an act-as impersonation of a registered user — mints an access-only token carrying that
    /// user's identity and roles (never the operator's), clamped to the remaining lifetime of the OPEN
    /// support session named by <see cref="ImpersonateUserRequest.AccessGrantId"/>. Justification is
    /// mandatory and audited. SaaS-only; rate-limited (S089a).
    /// </summary>
    [HttpPost("users/{userId:guid}/impersonate")]
    [Authorize(Policy = IamPermissionKeys.UserImpersonate)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<ImpersonationTokenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Impersonate(
        Guid userId,
        [FromBody] ImpersonateUserRequest req,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<ImpersonationTokenDto>(null!));
        return ResultResponse(
            await admin.StartImpersonationAsync(
                acting.Value,
                userId,
                req.AccessGrantId,
                req.Justification,
                ct
            )
        );
    }

    /// <summary>Ends an impersonation session — the minted token fails authentication on its next request.</summary>
    [NotDestructive(
        "Ends an impersonation session by stamping its end time; the audited grant row survives for the audit trail."
    )]
    [HttpDelete("impersonation/{accessGrantId:guid}")]
    [Authorize(Policy = IamPermissionKeys.UserImpersonate)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> EndImpersonation(Guid accessGrantId, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(await admin.EndImpersonationAsync(acting.Value, accessGrantId, ct));
    }

    /// <summary>Paged Plane-C audit search by principal/tenant/permission/outcome/time.</summary>
    [HttpGet("audit")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
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
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PagedList<IamAuditEntryDto>>(null!));

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<IamAuditEntryDto>> result = await admin.SearchAuditAsync(
            acting.Value,
            new(principalId, targetBroadcasterId, permission, outcome, from, to),
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Every LIVE per-tenant quota override.</summary>
    [HttpGet("tenants/{broadcasterId:guid}/limits")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.TenantRead)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TenantLimitOverrideDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListTenantLimitOverrides(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<IReadOnlyList<TenantLimitOverrideDto>>(null!));
        return ResultResponse(
            await admin.ListTenantLimitOverridesAsync(acting.Value, broadcasterId, ct)
        );
    }

    /// <summary>Sets (or replaces) the one LIVE override for one limit key on this tenant.</summary>
    [HttpPut("tenants/{broadcasterId:guid}/limits")]
    [Authorize(Policy = IamPermissionKeys.TenantQuotaManage)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<TenantLimitOverrideDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetTenantLimitOverride(
        Guid broadcasterId,
        [FromBody] SetTenantLimitOverrideRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<TenantLimitOverrideDto>(null!));
        return ResultResponse(
            await admin.SetTenantLimitOverrideAsync(acting.Value, broadcasterId, request, ct)
        );
    }

    /// <summary>Clears the LIVE override for one limit key, reverting to normal resolution.</summary>
    [NotDestructive(
        "Soft-deletes one TenantLimitOverride row; the tenant reverts to its normal tier/safety-baseline resolution and a new override can be set at any time."
    )]
    [HttpDelete("tenants/{broadcasterId:guid}/limits/{limitKey}")]
    [Authorize(Policy = IamPermissionKeys.TenantQuotaManage)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> ClearTenantLimitOverride(
        Guid broadcasterId,
        string limitKey,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(
            await admin.ClearTenantLimitOverrideAsync(acting.Value, broadcasterId, limitKey, ct)
        );
    }

    /// <summary>
    /// Forces a re-application of pending EF Core migrations — database-wide, audited against the tenant the
    /// operator was working on when they reached for it.
    /// </summary>
    [HttpPost("tenants/{broadcasterId:guid}/remigrate")]
    [Authorize(Policy = IamPermissionKeys.TenantRemigrate)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<TenantRemigrationResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForceRemigration(
        Guid broadcasterId,
        [FromBody] ForceRemigrationRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<TenantRemigrationResultDto>(null!));
        return ResultResponse(
            await admin.ForceRemigrationAsync(
                acting.Value,
                broadcasterId,
                request.Justification,
                ct
            )
        );
    }

    /// <summary>
    /// The counted blast radius of erasing this tenant whole — every tenant-scoped table, real row counts.
    /// The erase confirm MUST render this before the destructive action can be armed.
    /// </summary>
    [HttpGet("tenants/{broadcasterId:guid}/erase/preview")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.TenantErase)]
    [ProducesResponseType<StatusResponseDto<ChannelDeletePreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewEraseTenant(Guid broadcasterId, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<ChannelDeletePreviewDto>(null!));
        return ResultResponse(await admin.PreviewEraseTenantAsync(acting.Value, broadcasterId, ct));
    }

    /// <summary>
    /// Erases a tenant whole: a SOFT delete (30-day restore window) of the channel — the platform-operator
    /// sibling of the owner's own self-service delete, audited under the dedicated <c>tenant:erase</c> key.
    /// The blast radius shown by <see cref="PreviewEraseTenant"/> MUST be rendered before this is armed.
    /// </summary>
    [DestructiveAction(HasCountedBlastRadius = true)]
    [HttpPost("tenants/{broadcasterId:guid}/erase")]
    [Authorize(Policy = IamPermissionKeys.TenantErase)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> EraseTenant(
        Guid broadcasterId,
        [FromBody] EraseTenantRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(
            await admin.EraseTenantAsync(acting.Value, broadcasterId, request.Justification, ct)
        );
    }

    /// <summary>A machine-readable JSON export of every row this tenant owns — the operator sibling of the owner's own data.</summary>
    [HttpGet("tenants/{broadcasterId:guid}/export")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.TenantErase)]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportTenant(Guid broadcasterId, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<string>(null!));
        return ResultResponse(await admin.ExportTenantAsync(acting.Value, broadcasterId, ct));
    }

    /// <summary>
    /// The caller's IAM principal id for the service's audited re-check, via the shared resolver (fix D2 item
    /// 4) — a resolve failure DENIES rather than substituting <c>Guid.Empty</c>.
    /// </summary>
    private Task<Result<Guid>> ActingPrincipalIdAsync(CancellationToken ct) =>
        actingPrincipalResolver.ResolveActingPrincipalIdAsync(currentUser.UserId, ct);
}

/// <summary>Body for the reinstate action — the justification lands in the audit row.</summary>
public sealed record ReinstateTenantRequest(string Justification);

/// <summary>Body for forcing a re-migration — the justification lands in the audit row.</summary>
public sealed record ForceRemigrationRequest(string Justification);

/// <summary>Body for erasing a tenant whole — the justification lands in the audit row.</summary>
public sealed record EraseTenantRequest(string Justification);
