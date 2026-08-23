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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Plane-C platform IAM management (roles-permissions.md §5.4) — the admin panel's screen for promoting
/// accounts to platform principals and granting granular platform permissions. Each action carries the
/// Plane-C policy (policy name = <c>IamPermission.Key</c> verbatim, enforced by
/// <c>PlatformIamAuthorizationHandler</c>, audited on SaaS); the service re-asserts the same keys internally,
/// resolving the ACTING principal via <see cref="IIamCallerPrincipalResolverService"/> — a resolve failure
/// DENIES the action rather than substituting <c>Guid.Empty</c> (fix D2 item 4).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/platform/iam")]
[Authorize]
[Tags("Admin")]
public class PlatformIamController(
    IPlatformIamService iam,
    ICurrentUserService currentUser,
    IIamCallerPrincipalResolverService actingPrincipalResolver
) : BaseController
{
    /// <summary>The role catalog with each role's permission bundle — the role picker.</summary>
    [HttpGet("roles")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<IamRoleDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRoles(CancellationToken ct) =>
        ResultResponse(await iam.ListRolesAsync(ct));

    /// <summary>Every principal with its active role assignments — the IAM screen's principal list.</summary>
    [HttpGet("principals")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<IamPrincipalSummaryDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListPrincipals(CancellationToken ct) =>
        ResultResponse(await iam.ListPrincipalsAsync(ct));

    /// <summary>Effective permission keys for a principal (optionally within one tenant scope).</summary>
    [HttpGet("principals/{principalId:guid}/permissions")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<string>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEffectivePermissions(
        Guid principalId,
        [FromQuery] Guid? scopeChannelId,
        CancellationToken ct
    ) => ResultResponse(await iam.GetEffectivePermissionsAsync(principalId, scopeChannelId, ct));

    /// <summary>
    /// Provisions a principal — promotes an existing user (employee) or creates a service account (its key is
    /// returned ONCE, then only its hash exists). An employee promotion also sets the user's platform marker,
    /// so their <c>admin</c> role claim mints on the next token refresh — no re-login.
    /// </summary>
    [HttpPost("principals")]
    [Authorize(Policy = IamPermissionKeys.IamPrincipalCreate)]
    [ProducesResponseType<StatusResponseDto<IamPrincipalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreatePrincipal(
        [FromBody] CreatePrincipalRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<IamPrincipalDto>(null!));
        return ResultResponse(await iam.CreatePrincipalAsync(acting.Value, request, ct));
    }

    /// <summary>Deactivates a principal and clears the employee's platform marker (the demote). Self-deactivation is refused.</summary>
    [HttpPost("principals/{principalId:guid}/deactivate")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    public async Task<IActionResult> DeactivatePrincipal(
        Guid principalId,
        [FromQuery] string? reason,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(
            await iam.DeactivatePrincipalAsync(acting.Value, principalId, reason, ct)
        );
    }

    /// <summary>Reactivates a principal and restores the employee's platform marker.</summary>
    [HttpPost("principals/{principalId:guid}/reactivate")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    public async Task<IActionResult> ReactivatePrincipal(Guid principalId, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(await iam.ReactivatePrincipalAsync(acting.Value, principalId, ct));
    }

    /// <summary>Assigns a role to a principal, optionally tenant-scoped and time-boxed.</summary>
    [HttpPost("assignments")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<StatusResponseDto<IamRoleAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignRole(
        [FromBody] AssignIamRoleRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<IamRoleAssignmentDto>(null!));
        return ResultResponse(
            await iam.AssignRoleAsync(
                acting.Value,
                request.PrincipalId,
                request.RoleId,
                request.ScopeChannelId,
                request.ExpiresAt,
                request.Reason,
                ct
            )
        );
    }

    /// <summary>Revokes a role assignment (sets <c>RevokedAt</c>; already-revoked is a no-op).</summary>
    [HttpDelete("assignments/{assignmentId:guid}")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    public async Task<IActionResult> RevokeAssignment(
        Guid assignmentId,
        [FromQuery] string? reason,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(
            await iam.RevokeAssignmentAsync(acting.Value, assignmentId, reason, ct)
        );
    }

    /// <summary>
    /// The caller's IAM principal id for the service's internal re-check, via the shared resolver (fix D2 item
    /// 4) — a resolve failure DENIES rather than substituting <c>Guid.Empty</c>.
    /// </summary>
    private Task<Result<Guid>> ActingPrincipalIdAsync(CancellationToken ct) =>
        actingPrincipalResolver.ResolveActingPrincipalIdAsync(currentUser.UserId, ct);
}
