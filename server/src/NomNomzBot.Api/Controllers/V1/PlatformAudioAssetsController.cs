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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Middleware;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.PlatformContent;
using NomNomzBot.Application.DTOs;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The platform audio library — audio files no channel owns, which <c>sound_clip</c> templates name. Upload
/// runs the same file rules as a channel's clip upload. Plane-C platform-employee surface, like
/// <see cref="PlatformContentController"/>.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/platform/content/audio-assets")]
[PlatformPlane]
[Authorize]
[Tags("Admin")]
[EnableRateLimiting(RateLimitPolicyNames.Admin)]
public class PlatformAudioAssetsController(
    IPlatformAudioAssetService assets,
    ICurrentUserService currentUser,
    IIamCallerPrincipalResolverService actingPrincipalResolver
) : BaseController
{
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.ContentRead)]
    [ProducesResponseType<StatusResponseDto<PaginatedResponse<PlatformAudioAssetDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> List(
        [FromQuery] Models.PageRequestDto page,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PagedList<PlatformAudioAssetDto>>(null!));

        Result<PagedList<PlatformAudioAssetDto>> result = await assets.ListAsync(
            acting.Value,
            page.Page,
            page.Take,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, page);
    }

    /// <summary>Upload one audio file (multipart; mp3, ogg or wav, at most 10 MB and 3 minutes).</summary>
    [HttpPost]
    [Authorize(Policy = IamPermissionKeys.ContentAuthor)]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [RequestSizeLimit(11 * 1024 * 1024)] // 11 MB envelope (10 MB content + headers)
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StatusResponseDto<PlatformAudioAssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadPlatformAudioAssetFormModel form,
        CancellationToken ct
    )
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting.WithValue<PlatformAudioAssetDto>(null!));
        if (form.File is null || form.File.Length == 0)
            return BadRequestResponse("No file provided.");

        await using Stream content = form.File.OpenReadStream();
        return ResultResponse(
            await assets.UploadAsync(
                acting.Value,
                new UploadPlatformAudioAssetRequest(
                    form.DisplayName ?? string.Empty,
                    form.File.FileName,
                    content
                ),
                ct
            )
        );
    }

    /// <summary>Delete one audio file. Refused (409 <c>ASSET_IN_USE</c>) while a non-retired template names it.</summary>
    [HttpDelete("{id:guid}")]
    [NotDestructive(
        "Removes one platform audio file and nothing else: the delete is refused while any non-retired "
            + "template names the file, and a channel that installed a template holds its own copy of the "
            + "bytes, so no channel or template loses anything."
    )]
    [Authorize(Policy = IamPermissionKeys.ContentAuthor)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        Result<Guid> acting = await ActingPrincipalIdAsync(ct);
        if (acting.IsFailure)
            return ResultResponse(acting);
        return ResultResponse(await assets.DeleteAsync(acting.Value, id, ct));
    }

    private Task<Result<Guid>> ActingPrincipalIdAsync(CancellationToken ct) =>
        actingPrincipalResolver.ResolveActingPrincipalIdAsync(currentUser.UserId, ct);

    public sealed class UploadPlatformAudioAssetFormModel
    {
        /// <summary>The label admins pick the file by; blank = the file name without its extension.</summary>
        public string? DisplayName { get; set; }

        public IFormFile? File { get; set; }
    }
}
