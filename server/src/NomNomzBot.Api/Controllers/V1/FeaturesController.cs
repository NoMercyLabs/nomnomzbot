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
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages feature flags and toggles for a channel.</summary>
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

    /// <summary>List all features and their current enabled state for the channel.</summary>
    [HttpGet]
    [RequireAction("feature:read")]
    [ProducesResponseType<StatusResponseDto<List<FeatureStatusDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatures(string channelId, CancellationToken ct)
    {
        Result<List<FeatureStatusDto>> result = await _featureService.GetFeaturesAsync(
            channelId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Toggle a feature on or off for the channel.</summary>
    [HttpPost("{featureKey}/toggle")]
    [RequireAction("feature:write")]
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
