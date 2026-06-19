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
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/features")]
[Authorize]
[Tags("Features")]
public class FeaturesController : BaseController
{
    private readonly IFeatureService _featureService;

    public FeaturesController(IFeatureService featureService)
    {
        _featureService = featureService;
    }

    [HttpGet]
    [ProducesResponseType<StatusResponseDto<List<FeatureStatusDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatures(string channelId, CancellationToken ct)
    {
        Result<List<FeatureStatusDto>> result = await _featureService.GetFeaturesAsync(
            channelId,
            ct
        );
        return ResultResponse(result);
    }

    [HttpPost("{featureKey}/toggle")]
    [ProducesResponseType<StatusResponseDto<FeatureStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ToggleFeature(
        string channelId,
        string featureKey,
        CancellationToken ct
    )
    {
        Result<FeatureStatusDto> result = await _featureService.ToggleFeatureAsync(
            channelId,
            featureKey,
            ct
        );
        return ResultResponse(result);
    }
}
