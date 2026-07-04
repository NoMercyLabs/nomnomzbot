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
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The feature-flag admin write surface (rollout-updates §5) — Plane C, gated on the
/// <c>featureflag:write</c> IAM permission (stream-admin.md §5 platform rows; the policy name is the
/// <c>IamPermission.Key</c> verbatim, audited per check on SaaS). Drives staged rollout: set the global
/// definition + ramp, and set/clear per-tenant overrides (internal/beta opt-in, per-channel kill-switch).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/feature-flags")]
[Authorize(Policy = IamPermissionKeys.FeatureFlagWrite)]
[Tags("Feature Flags")]
public class FeatureFlagAdminController(
    IFeatureFlagAdminService flags,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>List all feature-flag definitions.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        ResultResponse(await flags.ListAsync(ct));

    /// <summary>Create or update a feature flag's global definition and rollout ramp.</summary>
    [HttpPut]
    public async Task<IActionResult> SetFlag(
        [FromBody] SetFeatureFlagRequest request,
        CancellationToken ct
    ) => ResultResponse(await flags.SetFlagAsync(request, Caller(), ct));

    /// <summary>Set a per-channel override for a feature flag (beta opt-in or kill-switch).</summary>
    [HttpPut("{flagKey}/overrides/{broadcasterId:guid}")]
    public async Task<IActionResult> SetOverride(
        string flagKey,
        Guid broadcasterId,
        [FromBody] SetFeatureFlagOverrideRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(await flags.SetOverrideAsync(flagKey, broadcasterId, request, Caller(), ct));

    /// <summary>Clear a channel's feature-flag override, returning it to the global ramp.</summary>
    [HttpDelete("{flagKey}/overrides/{broadcasterId:guid}")]
    public async Task<IActionResult> RemoveOverride(
        string flagKey,
        Guid broadcasterId,
        CancellationToken ct
    ) => ResultResponse(await flags.RemoveOverrideAsync(flagKey, broadcasterId, Caller(), ct));

    private Guid? Caller() => Guid.TryParse(currentUser.UserId, out Guid id) ? id : null;
}
