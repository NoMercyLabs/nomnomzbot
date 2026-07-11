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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.ViewerData.Dtos;
using NomNomzBot.Application.ViewerData.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The per-viewer key/value store (per-viewer-data.md §5) — the dashboard's browse/set/delete surface
/// over what pipelines write via <c>set_viewer_data</c>/<c>adjust_viewer_data</c>. The AGGREGATE viewer
/// profile is deliberately not served here (D2) — it stays with the analytics/community read surfaces.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/viewers/{viewerId:guid}/data")]
[Authorize]
[Tags("Viewer Data")]
public class ViewerDataController : BaseController
{
    private readonly IViewerDataService _viewerData;
    private readonly ICurrentTenantService _tenant;

    public ViewerDataController(IViewerDataService viewerData, ICurrentTenantService tenant)
    {
        _viewerData = viewerData;
        _tenant = tenant;
    }

    /// <summary>The viewer's full custom-data map for this channel.</summary>
    [RequireAction("viewerdata:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyDictionary<string, string>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> List(Guid viewerId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");
        return ResultResponse(await _viewerData.ListForViewerAsync(broadcasterId, viewerId, ct));
    }

    /// <summary>Set one key (upsert).</summary>
    [RequireAction("viewerdata:write")]
    [HttpPut("{key}")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        Guid viewerId,
        string key,
        [FromBody] SetViewerDatumRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result set = await _viewerData.SetAsync(broadcasterId, viewerId, key, request.Value, ct);
        return set.IsFailure
            ? ResultResponse(Result.Failure<bool>(set.ErrorMessage!, set.ErrorCode))
            : ResultResponse(Result.Success(true));
    }

    /// <summary>Delete one key.</summary>
    [RequireAction("viewerdata:write")]
    [HttpDelete("{key}")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid viewerId, string key, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result deleted = await _viewerData.DeleteAsync(broadcasterId, viewerId, key, ct);
        return deleted.IsFailure
            ? ResultResponse(Result.Failure<bool>(deleted.ErrorMessage!, deleted.ErrorCode))
            : ResultResponse(Result.Success(true));
    }
}
