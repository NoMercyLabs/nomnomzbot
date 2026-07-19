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
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Manages the channel's media asset library (images/audio for overlay widgets) and serves the assets
/// publicly for OBS browser sources. Same trust class and action keys as sound clips.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/assets")]
[Authorize]
[Tags("Assets")]
public sealed class AssetsController : BaseController
{
    private readonly IChannelAssetService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public AssetsController(
        IChannelAssetService service,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant
    )
    {
        _service = service;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    private bool TryGetIds(out Guid broadcasterId, out Guid userId)
    {
        broadcasterId = _currentTenant.BroadcasterId ?? Guid.Empty;
        if (broadcasterId == Guid.Empty || !Guid.TryParse(_currentUser.UserId, out userId))
        {
            userId = Guid.Empty;
            return false;
        }
        return true;
    }

    // ── GET /assets ──────────────────────────────────────────────────────────

    /// <summary>List all media assets for the channel, paginated.</summary>
    [HttpGet]
    [RequireAction("sounds:read")]
    [ProducesResponseType<PaginatedResponse<ChannelAssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] PageRequestDto pagination,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid _))
            return Unauthorized();

        Result<PagedList<ChannelAssetDto>> result = await _service.ListAsync(
            broadcasterId,
            new PaginationParams(pagination.Page, pagination.Take),
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });

        PagedList<ChannelAssetDto> page = result.Value;
        return Ok(
            new PaginatedResponse<ChannelAssetDto>
            {
                Data = page.Items,
                Total = page.TotalCount,
                HasMore = page.HasNextPage,
            }
        );
    }

    // ── GET /assets/{id} ──────────────────────────────────────────────────────

    /// <summary>Retrieve a media asset's metadata by ID.</summary>
    [HttpGet("{id:guid}")]
    [RequireAction("sounds:read")]
    [ProducesResponseType<StatusResponseDto<ChannelAssetDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid _))
            return Unauthorized();

        Result<ChannelAssetDto> result = await _service.GetAsync(broadcasterId, id, ct);
        return result.IsSuccess
            ? Ok(new StatusResponseDto<ChannelAssetDto> { Data = result.Value })
            : NotFound(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }

    // ── POST /assets (multipart upload — create or replace by name) ───────────

    /// <summary>Upload a media asset (multipart, max 8 MB). An existing asset with the same name is replaced.</summary>
    [HttpPost]
    [RequireAction("sounds:write")]
    [RequestSizeLimit(9 * 1024 * 1024)] // 9 MB envelope (8 MB content + headers)
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StatusResponseDto<ChannelAssetDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadChannelAssetFormModel form,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid userId))
            return Unauthorized();

        if (form.File is null || form.File.Length == 0)
            return BadRequest(new StatusResponseDto<object> { Message = "No file provided." });

        UploadChannelAssetRequest request = new(
            form.Name,
            form.DisplayName ?? form.Name,
            form.File.FileName,
            form.File.OpenReadStream()
        );

        Result<ChannelAssetDto> result = await _service.UploadAsync(
            broadcasterId,
            userId,
            request,
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });

        return CreatedAtAction(
            nameof(Get),
            new { id = result.Value.Id },
            new StatusResponseDto<ChannelAssetDto> { Data = result.Value }
        );
    }

    // ── DELETE /assets/{id} ───────────────────────────────────────────────────

    /// <summary>Delete a media asset (its serving URL stops resolving).</summary>
    [HttpDelete("{id:guid}")]
    [RequireAction("sounds:write")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid userId))
            return Unauthorized();

        Result result = await _service.DeleteAsync(broadcasterId, id, userId, ct);
        return result.IsSuccess
            ? Ok(new StatusResponseDto<bool> { Data = true })
            : NotFound(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }

    // ── GET /assets/file/{channelId}/{name} ───────────────────────────────────
    // The stable public serving route widget configs store. Anonymous — OBS browser sources have no JWT;
    // the asset is broadcaster-authored media for their own overlay (same trust class as sound clips).

    /// <summary>Serve an asset's bytes publicly (OBS browser sources); immutable-cached, sniffed type only.</summary>
    [HttpGet("file/{channelId:guid}/{name}")]
    [AllowAnonymous]
    public async Task<IActionResult> ServeFile(Guid channelId, string name, CancellationToken ct)
    {
        Result<ChannelAssetContent> result = await _service.OpenForServingAsync(
            channelId,
            name,
            ct
        );
        if (!result.IsSuccess)
            return NotFound();

        // Replace keeps the URL but the DTO's `?v=` cache-buster changes — so far-future caching is safe.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        // Never let a browser second-guess the sniffed type.
        Response.Headers.XContentTypeOptions = "nosniff";

        if (result.Value.MimeType == "image/svg+xml")
        {
            // SVG can carry script when opened as a document — lock it down so it can never execute:
            // no scripts, no external loads; inline styles only. <img>/CSS uses are unaffected.
            Response.Headers.ContentSecurityPolicy =
                "default-src 'none'; style-src 'unsafe-inline'; sandbox";
            Response.Headers.ContentDisposition = $"inline; filename=\"{name}.svg\"";
        }

        return File(result.Value.Content, result.Value.MimeType, enableRangeProcessing: true);
    }

    // ── Form model ────────────────────────────────────────────────────────────

    public sealed class UploadChannelAssetFormModel
    {
        public string Name { get; set; } = null!;
        public string? DisplayName { get; set; }
        public IFormFile? File { get; set; }
    }
}
