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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
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
[Route("api/v{version:apiVersion}/viewers/{viewerId}/data")]
[Authorize]
[Tags("Viewer Data")]
public class ViewerDataController : BaseController
{
    private readonly IViewerDataService _viewerData;
    private readonly ICurrentTenantService _tenant;
    private readonly IApplicationDbContext _db;

    public ViewerDataController(
        IViewerDataService viewerData,
        ICurrentTenantService tenant,
        IApplicationDbContext db
    )
    {
        _viewerData = viewerData;
        _tenant = tenant;
        _db = db;
    }

    /// <summary>The viewer's full custom-data map for this channel.</summary>
    [RequireAction("viewerdata:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyDictionary<string, string>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> List(string viewerId, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        if (await ResolveViewerUserIdAsync(viewerId, ct) is not Guid viewerUserId)
            return NotFoundResponse("Viewer not found.");

        return ResultResponse(
            await _viewerData.ListForViewerAsync(broadcasterId, viewerUserId, ct)
        );
    }

    /// <summary>Set one key (upsert).</summary>
    [RequireAction("viewerdata:write")]
    [HttpPut("{key}")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set(
        string viewerId,
        string key,
        [FromBody] SetViewerDatumRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        if (await ResolveViewerUserIdAsync(viewerId, ct) is not Guid viewerUserId)
            return NotFoundResponse("Viewer not found.");

        Result set = await _viewerData.SetAsync(
            broadcasterId,
            viewerUserId,
            key,
            request.Value,
            ct
        );
        return set.IsFailure
            ? ResultResponse(Result.Failure<bool>(set.ErrorMessage!, set.ErrorCode))
            : ResultResponse(Result.Success(true));
    }

    /// <summary>Delete one key.</summary>
    [RequireAction("viewerdata:write")]
    [HttpDelete("{key}")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(string viewerId, string key, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        if (await ResolveViewerUserIdAsync(viewerId, ct) is not Guid viewerUserId)
            return NotFoundResponse("Viewer not found.");

        Result deleted = await _viewerData.DeleteAsync(broadcasterId, viewerUserId, key, ct);
        return deleted.IsFailure
            ? ResultResponse(Result.Failure<bool>(deleted.ErrorMessage!, deleted.ErrorCode))
            : ResultResponse(Result.Success(true));
    }

    /// <summary>
    /// Resolve the route's <paramref name="viewerId"/> to an internal <see cref="Domain.Identity.Entities.User"/>
    /// id. The viewer-facing surfaces (community, chat, moderation) identify a viewer by their Twitch user id, so
    /// the endpoint accepts EITHER an internal id (a parseable <see cref="Guid"/>) OR a Twitch user id, which is
    /// resolved to the owning User — mirroring how <c>CommunityController</c> joins on <c>User.TwitchUserId</c>.
    /// Returns null when no such viewer exists (they have never been seen as a User).
    /// </summary>
    private async Task<Guid?> ResolveViewerUserIdAsync(string viewerId, CancellationToken ct)
    {
        if (Guid.TryParse(viewerId, out Guid internalId))
            return internalId;

        return await _db
            .Users.Where(u => u.TwitchUserId == viewerId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);
    }
}
