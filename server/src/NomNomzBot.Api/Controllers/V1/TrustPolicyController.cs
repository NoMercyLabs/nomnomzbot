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
using NomNomzBot.Application.Trust.Dtos;
using NomNomzBot.Application.Trust.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The channel's trust tuning (S-OWN23) — every weight, decay, penalty, tier ceiling and heat value the
/// bot uses to decide who it trusts and who it acts on. Values only: the plain-language explanation of
/// what each knob costs lives in the dashboard's i18n, never in the API.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels")]
[Tags("Trust")]
public class TrustPolicyController : BaseController
{
    private readonly ITrustPolicyService _trustPolicy;

    public TrustPolicyController(ITrustPolicyService trustPolicy)
    {
        _trustPolicy = trustPolicy;
    }

    /// <summary>
    /// The channel's trust tuning. A channel that has never edited it gets the shipped defaults with
    /// <c>isPinned: false</c>, so the dashboard can show what is a default and what the operator chose.
    /// </summary>
    [HttpGet("{channelId}/trust/policy")]
    [Authorize]
    [RequireAction("trust:policy:read")]
    [ProducesResponseType<StatusResponseDto<TrustPolicyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrustPolicy(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result<TrustPolicyDto> result = await _trustPolicy.GetForEditingAsync(tenantId, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// The shipped trust defaults (never pinned), so the editor can show "default" next to every
    /// value and offer a reset without carrying its own copy of the numbers.
    /// </summary>
    [HttpGet("{channelId}/trust/policy/defaults")]
    [Authorize]
    [RequireAction("trust:policy:read")]
    [ProducesResponseType<StatusResponseDto<TrustPolicyDto>>(StatusCodes.Status200OK)]
    public IActionResult GetTrustPolicyDefaults(string channelId)
    {
        if (!Guid.TryParse(channelId, out Guid _))
            return BadRequestResponse("Invalid channel id.");

        return ResultResponse(Result.Success(_trustPolicy.GetDefaults()));
    }

    /// <summary>
    /// Saves the channel's trust tuning, creating the row on first edit. Ranges are enforced
    /// server-side — the four weights must sum to 1.0, tier ceilings must ascend, and no penalty, decay
    /// or heat amount may be negative — so a policy that could not produce a sane score is rejected
    /// rather than stored.
    /// </summary>
    [HttpPut("{channelId}/trust/policy")]
    [Authorize]
    [RequireAction("trust:policy:manage")]
    [ProducesResponseType<StatusResponseDto<TrustPolicyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTrustPolicy(
        string channelId,
        [FromBody] UpdateTrustPolicyRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        Result<TrustPolicyDto> result = await _trustPolicy.UpdateAsync(tenantId, request, ct);
        return ResultResponse(result);
    }
}
