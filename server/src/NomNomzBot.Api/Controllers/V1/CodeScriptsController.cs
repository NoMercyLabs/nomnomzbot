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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Platform.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Custom-code authoring (custom-code.md §5) — management plane, Broadcaster floor, Critical tier
/// (<c>code:script:author</c>, per-user delegable via permit only). Tenant is resolved from the authenticated
/// principal (not the route — IDOR fix). The whole controller is gated behind the <c>custom_code</c> channel
/// feature; the broadcaster enables it on the Features page. No endpoint runs a script — execution is
/// the <c>run_code</c> pipeline action.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/code-scripts")]
[Authorize]
[RequireAction("code:script:author")]
[Tags("Custom Code")]
public class CodeScriptsController(
    ICodeScriptService scripts,
    IFeatureService featureService,
    ICurrentTenantService tenant
) : BaseController
{
    /// <summary>List the channel's code scripts, paginated (feature-gated like all script routes).</summary>
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

    /// <summary>Read a single code script by id.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.GetAsync(id, ct));
    }

    /// <summary>Create a new code script for the channel.</summary>
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

    /// <summary>Add a new source version to a code script.</summary>
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

    /// <summary>Load a script's current multi-file project (its <c>src/</c> file set + manifest) for the editor.</summary>
    [HttpGet("{id:guid}/project")]
    public async Task<IActionResult> GetProject(Guid id, CancellationToken ct)
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.GetProjectAsync(id, ct));
    }

    /// <summary>
    /// Save a script's multi-file project (file set + manifest): validate + compile the entry, then append and
    /// publish a new version on success. A validation or compile failure returns the reason and persists nothing.
    /// </summary>
    [HttpPut("{id:guid}/project")]
    public async Task<IActionResult> SaveProject(
        Guid id,
        [FromBody] ProjectDto project,
        CancellationToken ct
    )
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.SaveProjectAsync(id, project, ct));
    }

    /// <summary>List a code script's versions, paginated.</summary>
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

    /// <summary>Publish a specific version as the script's live version (the one run_code executes).</summary>
    [HttpPost("{id:guid}/versions/{versionId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, Guid versionId, CancellationToken ct)
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.PublishVersionAsync(id, versionId, ct));
    }

    /// <summary>Enable or disable a code script.</summary>
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

    /// <summary>Delete a code script.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        Result gate = await FeatureGateAsync(ct);
        return gate.IsFailure
            ? ResultResponse(gate)
            : ResultResponse(await scripts.DeleteAsync(id, ct));
    }

    private async Task<Result> FeatureGateAsync(CancellationToken ct)
    {
        string channelId = tenant.BroadcasterId?.ToString() ?? string.Empty;
        bool enabled = await featureService.IsFeatureEnabledAsync(channelId, "custom_code", ct);
        return enabled
            ? Result.Success()
            : Result.Failure(
                "Custom code is not enabled for this channel. Enable it on the Features page.",
                "FEATURE_DISABLED"
            );
    }
}
