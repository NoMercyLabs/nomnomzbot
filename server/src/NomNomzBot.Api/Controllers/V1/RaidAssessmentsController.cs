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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The vetting result of every raid the channel received: account age, earlier raids, ban and network-block
/// state, and the verdict. Read-only; the rows are written when the raid arrives.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/moderation/raid-assessments")]
[Authorize]
[Tags("Moderation")]
public class RaidAssessmentsController : BaseController
{
    private readonly IRaidAssessmentService _assessments;

    public RaidAssessmentsController(IRaidAssessmentService assessments)
    {
        _assessments = assessments;
    }

    /// <summary>List the channel's assessed raids, newest first, paginated.</summary>
    [RequireAction("moderation:queue:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<RaidAssessmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAssessments(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcaster))
            return BadRequestResponse("Invalid channel id.");

        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<RaidAssessmentDto>> result = await _assessments.ListRecentAsync(
            broadcaster,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }
}
