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
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Custom-code authoring (custom-code.md §5) — management plane, Broadcaster floor, Critical tier
/// (<c>code:script:author</c>, per-user delegable via permit only). Tenant is resolved from the authenticated
/// principal (not the route — IDOR fix). The whole controller is gated behind the <c>custom_code</c> feature flag;
/// no endpoint runs a script — execution is the <c>run_code</c> pipeline action.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/code-scripts")]
[Authorize]
[RequireAction("code:script:author")]
[Tags("Custom Code")]
public class CodeScriptsController(ICodeScriptService scripts, IFeatureFlagService featureFlags)
    : BaseController
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageRequestDto request, CancellationToken ct)
    {
        Result gate = await FeatureGateAsync(ct);
        if (gate.IsFailure)
            return ResultResponse(gate);
        Result<PagedList<CodeScriptSummaryDto>> result = await scripts.ListAsync(
            new PaginationParams(request.Page, request.Take, request.Sort, request.Order),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.GetAsync(id, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCodeScriptRequest request,
        CancellationToken ct
    )
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.CreateAsync(request, ct));
    }

    [HttpPost("{id:guid}/versions")]
    public async Task<IActionResult> CreateVersion(
        Guid id,
        [FromBody] CreateCodeScriptVersionRequest request,
        CancellationToken ct
    )
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.CreateVersionAsync(id, request, ct));
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> ListVersions(
        Guid id,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        Result gate = await FeatureGateAsync(ct);
        if (gate.IsFailure)
            return ResultResponse(gate);
        Result<PagedList<CodeScriptVersionDto>> result = await scripts.ListVersionsAsync(
            id,
            new PaginationParams(request.Page, request.Take, request.Sort, request.Order),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, Guid versionId, CancellationToken ct)
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.PublishVersionAsync(id, versionId, ct));
    }

    [HttpPatch("{id:guid}/enabled")]
    public async Task<IActionResult> SetEnabled(
        Guid id,
        [FromBody] SetCodeScriptEnabledRequest request,
        CancellationToken ct
    )
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.SetEnabledAsync(id, request.IsEnabled, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.DeleteAsync(id, ct));
    }

    private async Task<Result> FeatureGateAsync(CancellationToken ct) =>
        await featureFlags.IsEnabledAsync("custom_code", ct)
            ? Result.Success()
            : Result.Failure("Custom code is not enabled for this channel.", "FEATURE_DISABLED");
}
