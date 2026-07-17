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
using NomNomzBot.Application.Contracts.Gdpr;
using NomNomzBot.Application.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The self-service GDPR my-data plane (gdpr-crypto.md §5.1). Gate-1 only: any authenticated principal,
/// and the subject is ALWAYS the JWT <c>sub</c> — never read from the request body or route — so a caller
/// can only ever act on their own data. Erasing ANOTHER subject is a controller action on the audited
/// <c>ComplianceController</c> plane, never here.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/gdpr")]
[Authorize]
[Tags("GDPR")]
public class GdprController : BaseController
{
    private const string SelfService = "self_service";

    private readonly IErasureService _erasure;
    private readonly IConsentService _consents;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public GdprController(
        IErasureService erasure,
        IConsentService consents,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant
    )
    {
        _erasure = erasure;
        _consents = consents;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    /// <summary>The authenticated subject (JWT <c>sub</c>) — the only identity this plane operates on.</summary>
    private bool TryGetCaller(out Guid callerId) =>
        Guid.TryParse(_currentUser.UserId, out callerId);

    /// <summary>Export the caller's personal data as a machine-readable JSON document (right of access).</summary>
    [HttpGet("export")]
    [ProducesResponseType<StatusResponseDto<DataExportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportData(CancellationToken ct)
    {
        if (!TryGetCaller(out Guid callerId))
            return UnauthenticatedResponse();
        Result<DataExportDto> result = await _erasure.RequestExportAsync(
            new RequestExportRequest(callerId, _currentTenant.BroadcasterId, SelfService),
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Request erasure of the caller's own data (right to be forgotten). Irreversible.</summary>
    [HttpPost("erasure")]
    [ProducesResponseType<StatusResponseDto<ErasureRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestErasure(
        [FromBody] RequestErasureRequest? request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid callerId))
            return UnauthenticatedResponse();
        // Subject + requester are forced to the caller; tenant context comes from the JWT — only the
        // requested scope is honored from the body.
        Result<ErasureRequestDto> result = await _erasure.RequestErasureAsync(
            new RequestErasureRequest(
                callerId,
                _currentTenant.BroadcasterId,
                SelfService,
                request?.Scope ?? "deployment"
            ),
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Opt out of legitimate-interest processing (marketing / leaderboards / analytics).</summary>
    [HttpPost("opt-out")]
    [ProducesResponseType<StatusResponseDto<ErasureRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestOptOut(CancellationToken ct)
    {
        if (!TryGetCaller(out Guid callerId))
            return UnauthenticatedResponse();
        Result<ErasureRequestDto> result = await _erasure.RequestOptOutAsync(
            new RequestOptOutRequest(callerId, _currentTenant.BroadcasterId, SelfService),
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>List the caller's own GDPR requests, newest first.</summary>
    [HttpGet("requests")]
    [ProducesResponseType<PaginatedResponse<ErasureRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRequests(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid callerId))
            return UnauthenticatedResponse();
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<ErasureRequestDto>> result = await _erasure.ListRequestsAsync(
            pagination,
            subjectUserId: callerId,
            broadcasterId: null,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Read one of the caller's own GDPR requests. A foreign request reads as not found.</summary>
    [HttpGet("requests/{id:guid}")]
    [ProducesResponseType<StatusResponseDto<ErasureRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRequest(Guid id, CancellationToken ct)
    {
        if (!TryGetCaller(out Guid callerId))
            return UnauthenticatedResponse();
        Result<ErasureRequestDto> result = await _erasure.GetRequestAsync(id, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        // Existence of another subject's request is itself personal data — a foreign id 404s, never 403s.
        if (result.Value.SubjectUserId != callerId)
            return NotFoundResponse("The request was not found.");
        return ResultResponse(result);
    }

    /// <summary>List the caller's own consent ledger (every channel + platform-wide rows).</summary>
    [HttpGet("consents")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ConsentRecordDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListConsents(CancellationToken ct)
    {
        if (!TryGetCaller(out Guid callerId))
            return UnauthenticatedResponse();
        Result<IReadOnlyList<ConsentRecordDto>> result = await _consents.ListForSubjectAsync(
            callerId,
            broadcasterId: null,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Grant a consent for the caller themselves (subject forced to the JWT <c>sub</c>).</summary>
    [HttpPost("consents")]
    [ProducesResponseType<StatusResponseDto<ConsentRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GrantConsent(
        [FromBody] GrantConsentRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid callerId))
            return UnauthenticatedResponse();
        Result<ConsentRecordDto> result = await _consents.GrantAsync(
            request with
            {
                SubjectUserId = callerId,
            },
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Withdraw one of the caller's own consents (optionally scoped to a channel).</summary>
    [HttpDelete("consents/{consentType}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> WithdrawConsent(
        string consentType,
        [FromQuery] Guid? broadcasterId,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid callerId))
            return UnauthenticatedResponse();
        Result result = await _consents.WithdrawAsync(callerId, broadcasterId, consentType, ct);
        return ResultResponse(result);
    }
}
