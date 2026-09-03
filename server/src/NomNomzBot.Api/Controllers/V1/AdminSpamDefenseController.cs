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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Platform-wide spam-defence defaults (spam-defense.md §6) — the values every new channel inherits,
/// and that every channel which has never edited its own settings keeps tracking.
///
/// <para><b>Same editor, different scope.</b> The response shape is identical to the per-channel one so
/// the dashboard renders both with one component: learning the channel page teaches the admin page, and
/// a knob cannot exist on one and be missing from the other.</para>
///
/// <para>Gated on <c>featureflag:write</c> rather than a new permission, because that key already means
/// exactly this: change a platform-wide setting that affects every channel at once.</para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/spam-defense")]
[Tags("Admin")]
public class AdminSpamDefenseController : BaseController
{
    private readonly ISpamDefenseService _spamDefense;

    public AdminSpamDefenseController(ISpamDefenseService spamDefense)
    {
        _spamDefense = spamDefense;
    }

    /// <summary>The shipped defaults, or whatever the platform has set them to.</summary>
    [HttpGet("defaults")]
    [Authorize(Policy = IamPermissionKeys.FeatureFlagWrite)]
    [ProducesResponseType<StatusResponseDto<SpamDefensePolicyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDefaults(CancellationToken ct) =>
        Ok(
            new StatusResponseDto<SpamDefensePolicyDto>
            {
                Data = await _spamDefense.GetPolicyAsync(
                    SpamDefenseService.PlatformDefaultsScope,
                    ct
                ),
            }
        );

    /// <summary>
    /// Saves the platform defaults. Validated against exactly the same ranges as a channel's own
    /// settings — a default nobody could save on a channel page must not be settable here either.
    /// </summary>
    [HttpPut("defaults")]
    [Authorize(Policy = IamPermissionKeys.FeatureFlagWrite)]
    [ProducesResponseType<StatusResponseDto<SpamDefenseSettings>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDefaults(
        [FromBody] SpamDefenseSettings settings,
        CancellationToken ct
    )
    {
        Result<SpamDefenseSettings> result = await _spamDefense.UpdateSettingsAsync(
            SpamDefenseService.PlatformDefaultsScope,
            settings,
            ct
        );

        return ResultResponse(result);
    }
}
