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
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.DTOs;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Plane-C platform content authoring + propagation (platform-admin.md §4) — draft/publish versioned
/// system commands and fan them out to installed tenants under one of the three publish modes (§2.1),
/// blast radius shown before a publish commits. SaaS-only platform-employee surface (§0 marker); not a
/// channel route, matching <c>PlatformIamController</c>/<c>PlatformAnalyticsController</c>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/platform/content")]
[Authorize]
[Tags("Admin")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class PlatformContentController(
    IPlatformContentService content,
    ICurrentUserService currentUser,
    IIamCallerPrincipalResolverService actingPrincipalResolver
) : BaseController
{
    [HttpGet("definitions")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.ContentRead)]
    [ProducesResponseType<StatusResponseDto<PaginatedResponse<PlatformContentDefinitionDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListDefinitions(
        [FromQuery] string? kind,
        [FromQuery] NomNomzBot.Api.Models.PageRequestDto page,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PagedList<PlatformContentDefinitionDto>>(null!));

        Result<PagedList<PlatformContentDefinitionDto>> result = await content.ListDefinitionsAsync(
            acting.Value,
            kind,
            page.Page,
            page.Take,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, page);
    }

    [HttpGet("definitions/{id:guid}")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.ContentRead)]
    [ProducesResponseType<StatusResponseDto<PlatformContentDefinitionDetailDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDefinition(Guid id, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PlatformContentDefinitionDetailDto>(null!));
        return ResultResponse(await content.GetDefinitionAsync(acting.Value, id, ct));
    }

    [HttpPost("definitions")]
    [Authorize(Policy = IamPermissionKeys.ContentAuthor)]
    [ProducesResponseType<StatusResponseDto<PlatformContentDefinitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateDefinition(
        [FromBody] CreateContentDefinitionRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PlatformContentDefinitionDto>(null!));
        return ResultResponse(await content.CreateDefinitionAsync(acting.Value, request, ct));
    }

    [HttpPost("definitions/{id:guid}/versions")]
    [Authorize(Policy = IamPermissionKeys.ContentAuthor)]
    [ProducesResponseType<StatusResponseDto<PlatformContentVersionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DraftVersion(
        Guid id,
        [FromBody] DraftContentVersionRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PlatformContentVersionDto>(null!));
        return ResultResponse(await content.DraftVersionAsync(acting.Value, id, request, ct));
    }

    [HttpGet("definitions/{id:guid}/versions/{versionId:guid}")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.ContentRead)]
    [ProducesResponseType<StatusResponseDto<PlatformContentVersionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PlatformContentVersionDto>(null!));
        return ResultResponse(await content.GetVersionAsync(acting.Value, id, versionId, ct));
    }

    /// <summary>Runs the exact same selection query the publish will use and returns the blast radius — the
    /// count that must render before a confirm button enables (§2.1).</summary>
    [HttpPost("definitions/{id:guid}/versions/{versionId:guid}/publish-preview")]
    [Authorize(Policy = IamPermissionKeys.ContentAuthor)]
    [ProducesResponseType<StatusResponseDto<PublishPreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewPublish(
        Guid id,
        Guid versionId,
        [FromBody] PublishPreviewRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PublishPreviewDto>(null!));
        return ResultResponse(
            await content.PreviewPublishAsync(acting.Value, id, versionId, request.Mode, ct)
        );
    }

    /// <summary>Fans a version out to tenants per the chosen mode. <c>Mode = force</c> requires
    /// <c>content:publish:force</c> — the one mode capable of overwriting a tenant's own edits (§2.1). The
    /// <c>ConfirmedPreviewAffectedCount</c> must byte-match the most recent preview or this fails closed
    /// with <c>PREVIEW_STALE</c>.</summary>
    [HttpPost("definitions/{id:guid}/versions/{versionId:guid}/publish")]
    [Authorize(Policy = IamPermissionKeys.ContentPublish)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<PlatformContentPublishJobDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Publish(
        Guid id,
        Guid versionId,
        [FromBody] PublishContentRequest request,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PlatformContentPublishJobDto>(null!));
        return ResultResponse(await content.PublishAsync(acting.Value, id, versionId, request, ct));
    }

    [HttpGet("publish-jobs/{id:guid}")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.ContentRead)]
    [ProducesResponseType<StatusResponseDto<PlatformContentPublishJobDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublishJob(Guid id, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PlatformContentPublishJobDto>(null!));
        return ResultResponse(await content.GetPublishJobAsync(acting.Value, id, ct));
    }

    /// <summary>Sets <c>RetiredAt</c>; never touches already-installed tenant copies (§3.1).</summary>
    [HttpDelete("definitions/{id:guid}")]
    [Authorize(Policy = IamPermissionKeys.ContentAuthor)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    public async Task<IActionResult> RetireDefinition(Guid id, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(await content.RetireDefinitionAsync(acting.Value, id, ct));
    }

    private Task<Result<Guid>> ActingPrincipalIdAsync(CancellationToken ct) =>
        actingPrincipalResolver.ResolveActingPrincipalIdAsync(currentUser.UserId, ct);
}
