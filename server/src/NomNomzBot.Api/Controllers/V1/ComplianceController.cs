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
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class ComplianceController : BaseController
{
    private readonly IErasureService _erasure;

    public ComplianceController(IErasureService erasure) => _erasure = erasure;

    /// <summary>
    /// Erase a subject's data on their behalf (broadcaster- or platform-initiated). The requester kind is
    /// constrained to the operator plane — a body claiming <c>self_service</c> is rejected by validation.
    /// Gated on <c>compliance:erasure</c> — a destructive, irreversible action distinct from the
    /// support-visit <c>tenant:access</c> key; holding tenant:access alone must not permit erasure.
    /// </summary>
    /// <summary>
    /// Counted preview of what erasing <paramref name="subjectUserId"/> would destroy (S-CONSEQ) — the same
    /// real row counts the operator confirm surface must render before the irreversible erasure. Read-only.
    /// </summary>
    [HttpGet("erasure/preview")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.ComplianceErasure)]
    [ProducesResponseType<StatusResponseDto<ErasurePreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewErasure(
        [FromQuery] Guid subjectUserId,
        [FromQuery] Guid? broadcasterId,
        CancellationToken ct
    )
    {
        Result<ErasurePreviewDto> result = await _erasure.PreviewErasureAsync(
            new(subjectUserId, broadcasterId),
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Export a subject's personal data on their behalf (right of access, operator-initiated).
    ///
    /// <para>The self-service <c>GET /gdpr/export</c> only ever exports the CALLER's own data, so an
    /// access request that arrives to the operator rather than from the subject's own dashboard had no
    /// route to fulfil it. This is that route, and it is the operator sibling of
    /// <see cref="PreviewErasure"/> — same plane, same key, same read-only nature.</para>
    ///
    /// <para>Gated on <c>compliance:erasure</c>, the compliance plane's key: the erasure preview beside
    /// it already reads a subject's personal-data counts under exactly that gate, and exporting a
    /// stranger's personal data is not something a channel moderator does in passing.</para>
    /// </summary>
    [HttpGet("export")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.ComplianceErasure)]
    [ProducesResponseType<StatusResponseDto<DataExportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportSubjectData(
        [FromQuery] Guid subjectUserId,
        [FromQuery] Guid? broadcasterId,
        CancellationToken ct
    )
    {
        Result<DataExportDto> result = await _erasure.RequestExportAsync(
            // "platform_iam" rather than "self_service": the ledger must record that an operator
            // fulfilled this, not that the subject asked from their own dashboard.
            new(subjectUserId, broadcasterId, "platform_iam"),
            ct
        );
        return ResultResponse(result);
    }

    [HttpPost("erasure")]
    [DestructiveAction(HasCountedBlastRadius = true)]
    [Authorize(Policy = IamPermissionKeys.ComplianceErasure)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
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
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
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
