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
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.DTOs;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The channel side of platform templates: browse the published catalogue for one kind (event responses,
/// timers, …) and install a copy into this channel. Install is gated in the service by the kind's own
/// write action key (e.g. <c>eventresponses:write</c>) in the target channel.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/platform-templates")]
[Authorize]
[Tags("PlatformTemplates")]
public class PlatformTemplatesController(
    IPlatformTemplateCatalogService templates,
    ICurrentUserService currentUser
) : BaseController
{
    [HttpGet]
    [RequireAction("dashboard:read")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [ProducesResponseType<StatusResponseDto<PaginatedResponse<PlatformTemplateDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> List(
        string channelId,
        [FromQuery] string kind,
        [FromQuery] Models.PageRequestDto page,
        CancellationToken ct
    )
    {
        Result<PagedList<PlatformTemplateDto>> result = await templates.ListAsync(
            kind,
            page.Page,
            page.Take,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, page);
    }

    [HttpPost("{definitionId:guid}/install")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [ProducesResponseType<StatusResponseDto<InstalledPlatformTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Install(
        string channelId,
        Guid definitionId,
        [FromBody] InstallPlatformTemplateRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(currentUser.UserId, out Guid caller))
            return UnauthenticatedResponse();

        return ResultResponse(
            await templates.InstallAsync(caller, broadcasterId, definitionId, request, ct)
        );
    }
}
