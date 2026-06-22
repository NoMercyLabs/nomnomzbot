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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Platform;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The feature-flag admin write surface (rollout-updates §5) — Plane C (admin). Drives staged rollout: set the
/// global definition + ramp, and set/clear per-tenant overrides (internal/beta opt-in, per-channel kill-switch).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/feature-flags")]
[Authorize(Roles = "admin")]
[Tags("Feature Flags")]
public class FeatureFlagAdminController(
    IFeatureFlagAdminService flags,
    ICurrentUserService currentUser
) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        ResultResponse(await flags.ListAsync(ct));

    [HttpPut]
    public async Task<IActionResult> SetFlag(
        [FromBody] SetFeatureFlagRequest request,
        CancellationToken ct
    ) => ResultResponse(await flags.SetFlagAsync(request, Caller(), ct));

    [HttpPut("{flagKey}/overrides/{broadcasterId:guid}")]
    public async Task<IActionResult> SetOverride(
        string flagKey,
        Guid broadcasterId,
        [FromBody] SetFeatureFlagOverrideRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(await flags.SetOverrideAsync(flagKey, broadcasterId, request, Caller(), ct));

    [HttpDelete("{flagKey}/overrides/{broadcasterId:guid}")]
    public async Task<IActionResult> RemoveOverride(
        string flagKey,
        Guid broadcasterId,
        CancellationToken ct
    ) => ResultResponse(await flags.RemoveOverrideAsync(flagKey, broadcasterId, Caller(), ct));

    private Guid? Caller() => Guid.TryParse(currentUser.UserId, out Guid id) ? id : null;
}
