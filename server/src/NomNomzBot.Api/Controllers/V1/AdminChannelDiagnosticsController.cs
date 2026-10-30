// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Middleware;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Admin.Services;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The admin read of one channel's data (S-ADMIN-DIAG-READ): one parameterized endpoint whose <c>sections</c>
/// option picks registered section classes, plus one run's detail. Read-only, so no reason is asked (Stoney,
/// 2026-10-30); gated on <c>tenant:read</c> like the other admin channel reads, and the service records each
/// read in the operator's internal IAM audit.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/channels/{broadcasterId:guid}/diagnostics")]
[PlatformPlane]
[Authorize]
[Tags("Admin")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class AdminChannelDiagnosticsController(
    IChannelDiagnosticsService diagnostics,
    ICurrentUserService currentUser,
    IIamCallerPrincipalResolverService actingPrincipalResolver
) : BaseController
{
    /// <summary>
    /// The requested sections of one channel. <paramref name="sections"/> is a comma-separated list of section
    /// keys (<c>summary</c>, <c>runs</c>; all when omitted); an unknown key is a 400 naming it. The run list
    /// takes the channel run history's options: <c>page</c>, <c>pageSize</c>, <c>failuresOnly</c> and
    /// <c>pipelineId</c>.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.TenantRead)]
    [ProducesResponseType<StatusResponseDto<ChannelDiagnosticsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDiagnostics(
        Guid broadcasterId,
        [FromQuery] string? sections,
        [FromQuery] PageRequestDto request,
        [FromQuery] bool failuresOnly,
        [FromQuery] Guid? pipelineId,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<ChannelDiagnosticsDto>(null!));

        string[]? keys = sections?.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        PaginationParams runPage = new(request.Page, request.Take, request.Sort, request.Order);
        ChannelDiagnosticRequest query = new(broadcasterId, runPage, failuresOnly, pipelineId);
        return ResultResponse(await diagnostics.GetAsync(acting.Value, query, keys, ct));
    }

    /// <summary>One run of the channel, with its ordered step logs, so the failing step is identifiable.</summary>
    [HttpGet("runs/{runId:long}")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.TenantRead)]
    [ProducesResponseType<StatusResponseDto<PipelineExecutionDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRun(Guid broadcasterId, long runId, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PipelineExecutionDetailDto>(null!));
        return ResultResponse(
            await diagnostics.GetRunAsync(acting.Value, broadcasterId, runId, ct)
        );
    }

    private Task<Result<Guid>> ActingPrincipalIdAsync(CancellationToken ct) =>
        actingPrincipalResolver.ResolveActingPrincipalIdAsync(currentUser.UserId, ct);
}
