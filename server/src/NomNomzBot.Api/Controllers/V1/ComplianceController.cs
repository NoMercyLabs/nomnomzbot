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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Gdpr;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The operator/admin compliance plane (gdpr-crypto.md §5.2) — Plane-C IAM gated: the policy name IS the
/// seeded <c>IamPermissions</c> key verbatim, routed through <c>IPlatformIamService</c> and audited on SaaS.
/// Erasing another subject (a controller action under Art. 4(7)) happens HERE, never on the self-service
/// <c>GdprController</c> plane.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/compliance")]
[Tags("Compliance")]
public class ComplianceController : BaseController
{
    private readonly IErasureService _erasure;

    public ComplianceController(IErasureService erasure) => _erasure = erasure;

    /// <summary>
    /// Erase a subject's data on their behalf (broadcaster- or platform-initiated). The requester kind is
    /// constrained to the operator plane — a body claiming <c>self_service</c> is rejected by validation.
    /// </summary>
    [HttpPost("erasure")]
    [Authorize(Policy = IamPermissionKeys.TenantAccess)]
    [ProducesResponseType<StatusResponseDto<ErasureRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestErasure(
        [FromBody] RequestErasureRequest request,
        CancellationToken ct
    )
    {
        string requestedBy = request.RequestedBy switch
        {
            "broadcaster" => "broadcaster",
            _ => "platform_iam",
        };
        Result<ErasureRequestDto> result = await _erasure.RequestErasureAsync(
            request with
            {
                RequestedBy = requestedBy,
            },
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Page all subjects' GDPR requests (compliance audit view), newest first.</summary>
    [HttpGet("erasure")]
    [Authorize(Policy = IamPermissionKeys.AuditRead)]
    [ProducesResponseType<PaginatedResponse<ErasureRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListErasureRequests(
        [FromQuery] PageRequestDto request,
        [FromQuery] Guid? broadcasterId,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<ErasureRequestDto>> result = await _erasure.ListRequestsAsync(
            pagination,
            subjectUserId: null,
            broadcasterId,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }
}
