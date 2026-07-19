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
using NomNomzBot.Application.Sound.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages audio clips for sound effects and notifications.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sound-clips")]
[Authorize]
[Tags("Sound")]
public sealed class SoundClipsController : BaseController
{
    private readonly ISoundClipService _service;
    private readonly ISoundClipStore _store;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _currentTenant;

    public SoundClipsController(
        ISoundClipService service,
        ISoundClipStore store,
        ICurrentUserService currentUser,
        ICurrentTenantService currentTenant
    )
    {
        _service = service;
        _store = store;
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

    // ── GET /sound-clips ─────────────────────────────────────────────────────

    /// <summary>List all sound clips for the channel, paginated.</summary>
    [HttpGet]
    [RequireAction("sounds:read")]
    [ProducesResponseType<PaginatedResponse<SoundClipDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] PageRequestDto pagination,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid _))
            return Unauthorized();

        Result<PagedList<SoundClipDto>> result = await _service.ListAsync(
            broadcasterId,
            new PaginationParams(pagination.Page, pagination.Take),
            ct
        );

        if (!result.IsSuccess)
            return BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });

        PagedList<SoundClipDto> page = result.Value;
        return Ok(
            new PaginatedResponse<SoundClipDto>
            {
                Data = page.Items,
                Total = page.TotalCount,
                HasMore = page.HasNextPage,
            }
        );
    }

    // ── GET /sound-clips/{id} ─────────────────────────────────────────────────

    /// <summary>Retrieve a sound clip by ID.</summary>
    [HttpGet("{id:guid}")]
    [RequireAction("sounds:read")]
    [ProducesResponseType<StatusResponseDto<SoundClipDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid _))
            return Unauthorized();

        Result<SoundClipDto> result = await _service.GetAsync(broadcasterId, id, ct);
        return result.IsSuccess
            ? Ok(new StatusResponseDto<SoundClipDto> { Data = result.Value })
            : NotFound(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }

    // ── POST /sound-clips (multipart upload) ─────────────────────────────────

    /// <summary>Upload a new sound clip (multipart audio file, max 10 MB).</summary>
    [HttpPost]
    [RequireAction("sounds:write")]
    [RequestSizeLimit(11 * 1024 * 1024)] // 11 MB envelope (10 MB content + headers)
    [Consumes("multipart/form-data")]
    [ProducesResponseType<StatusResponseDto<SoundClipDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadSoundClipFormModel form,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid userId))
            return Unauthorized();

        if (form.File is null || form.File.Length == 0)
            return BadRequest(new StatusResponseDto<object> { Message = "No file provided." });

        UploadSoundClipRequest request = new(
            form.Name,
            form.DisplayName ?? form.Name,
            form.File.FileName,
            form.File.ContentType,
            form.File.OpenReadStream(),
            form.DefaultVolume,
            form.CooldownSeconds,
            form.MinPermissionLevel,
            form.TriggerWord
        );

        Result<SoundClipDto> result = await _service.UploadAsync(
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
            new StatusResponseDto<SoundClipDto> { Data = result.Value }
        );
    }

    // ── PUT /sound-clips/{id} ─────────────────────────────────────────────────

    /// <summary>Update sound clip metadata (name, display name, volume).</summary>
    [HttpPut("{id:guid}")]
    [RequireAction("sounds:write")]
    [ProducesResponseType<StatusResponseDto<SoundClipDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateSoundClipRequest body,
        CancellationToken ct
    )
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid userId))
            return Unauthorized();

        Result<SoundClipDto> result = await _service.UpdateAsync(
            broadcasterId,
            id,
            userId,
            body,
            ct
        );

        return result.IsSuccess
            ? Ok(new StatusResponseDto<SoundClipDto> { Data = result.Value })
            : NotFound(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }

    // ── DELETE /sound-clips/{id} ─────────────────────────────────────────────

    /// <summary>Delete a sound clip.</summary>
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

    // ── POST /sound-clips/{id}/preview ────────────────────────────────────────

    /// <summary>Play preview of sound clip (for testing before use).</summary>
    [HttpPost("{id:guid}/preview")]
    [RequireAction("sounds:write")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        if (!TryGetIds(out Guid broadcasterId, out Guid _))
            return Unauthorized();

        Result result = await _service.PreviewAsync(broadcasterId, id, ct);
        return result.IsSuccess
            ? Ok(new StatusResponseDto<bool> { Data = true })
            : BadRequest(new StatusResponseDto<object> { Message = result.ErrorMessage });
    }

    // ── GET /sound-clips/stream/{*storageKey} ────────────────────────────────
    // Serves the clip audio file for overlay playback. Anonymous — the overlay has no JWT;
    // the storage key is opaque and per-broadcaster (no cross-channel disclosure risk).

    /// <summary>Stream the clip's audio file for overlay playback; anonymous, range-request enabled.</summary>
    [HttpGet("stream/{*storageKey}")]
    [AllowAnonymous]
    public async Task<IActionResult> Stream(string storageKey, CancellationToken ct)
    {
        // Prevent path traversal.
        if (storageKey.Contains(".."))
            return BadRequest();

        Result<Stream> result = await _store.OpenAsync(storageKey, ct);
        if (!result.IsSuccess)
            return NotFound();

        string mimeType = GuessMimeFromKey(storageKey);
        return File(result.Value, mimeType, enableRangeProcessing: true);
    }

    private static string GuessMimeFromKey(string key)
    {
        if (key.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            return "audio/mpeg";
        if (key.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return "audio/ogg";
        if (key.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return "audio/wav";
        return "application/octet-stream";
    }

    // ── Form model ────────────────────────────────────────────────────────────

    public sealed class UploadSoundClipFormModel
    {
        public string Name { get; set; } = null!;
        public string? DisplayName { get; set; }
        public int DefaultVolume { get; set; } = 80;

        /// <summary>Global per-clip cooldown (seconds) for the chat soundboard trigger; 0 = none.</summary>
        public int CooldownSeconds { get; set; }

        /// <summary>Minimum community-standing ladder level to fire the chat trigger (0 = everyone).</summary>
        public int MinPermissionLevel { get; set; }

        /// <summary>Optional bare, prefix-less chat trigger word; blank = no chat trigger.</summary>
        public string? TriggerWord { get; set; }

        public IFormFile? File { get; set; }
    }
}
