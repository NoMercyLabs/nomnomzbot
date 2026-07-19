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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages a channel's text-to-speech provider, voice, and playback settings, for the dashboard operator configuring chat-triggered TTS.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/tts")]
[Authorize]
[Tags("TTS")]
public class TtsConfigController : BaseController
{
    private readonly ITtsConfigService _ttsConfigService;
    private readonly ITtsLexiconService _ttsLexiconService;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public TtsConfigController(
        ITtsConfigService ttsConfigService,
        ITtsLexiconService ttsLexiconService,
        IApplicationDbContext db,
        ICurrentUserService currentUser
    )
    {
        _ttsConfigService = ttsConfigService;
        _ttsLexiconService = ttsLexiconService;
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>Get the channel's TTS configuration.</summary>
    [HttpGet("config")]
    [RequireAction("tts:config:read")]
    [ProducesResponseType<StatusResponseDto<TtsConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfig(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsConfigDto> result = await _ttsConfigService.GetConfigAsync(broadcasterId, ct);
        return ResultResponse(result);
    }

    /// <summary>Update the channel's TTS configuration.</summary>
    [HttpPut("config")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<TtsConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig(
        string channelId,
        [FromBody] UpdateTtsConfigDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsConfigDto> result = await _ttsConfigService.UpdateConfigAsync(
            broadcasterId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TtsConfigDto> { Data = result.Value });
    }

    /// <summary>Store a BYOK provider API key (azure | elevenlabs). Vault-encrypted at rest; never echoed back.</summary>
    [HttpPut("config/byok/{provider}")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<TtsConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetByokKey(
        string channelId,
        string provider,
        [FromBody] SetTtsByokKeyDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsConfigDto> result = await _ttsConfigService.SetByokKeyAsync(
            broadcasterId,
            provider,
            request,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Remove a stored BYOK key so the provider falls back to the operator/app configuration.</summary>
    [HttpDelete("config/byok/{provider}")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<TtsConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearByokKey(
        string channelId,
        string provider,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsConfigDto> result = await _ttsConfigService.ClearByokKeyAsync(
            broadcasterId,
            provider,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Search the voice catalogue: free-text <c>q</c> matches name/display-name/description/tags; <c>locale</c>,
    /// <c>gender</c>, <c>provider</c>, and <c>accent</c> are equality filters. Paged (default 50). Backs the
    /// dashboard voice picker so a channel can browse thousands of voices instead of one flat list.
    /// </summary>
    [HttpGet("voices")]
    [RequireAction("tts:voice:read")]
    [ProducesResponseType<PaginatedResponse<TtsVoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVoices(
        string channelId,
        [FromQuery] string? q = null,
        [FromQuery] string? locale = null,
        [FromQuery] string? gender = null,
        [FromQuery] string? provider = null,
        [FromQuery] string? accent = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default
    )
    {
        Result<PagedList<TtsVoiceDto>> result = await _ttsConfigService.SearchVoicesAsync(
            new TtsVoiceQuery(q, locale, gender, provider, accent, page, pageSize),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        PagedList<TtsVoiceDto> paged = result.Value;
        return Ok(
            new PaginatedResponse<TtsVoiceDto>
            {
                Data = paged.Items,
                NextPage = paged.HasNextPage ? paged.Page + 1 : null,
                HasMore = paged.HasNextPage,
            }
        );
    }

    /// <summary>Generate a short test TTS clip to preview a voice.</summary>
    [HttpPost("test")]
    [RequireAction("tts:voice:test")]
    [ProducesResponseType<StatusResponseDto<TtsTestResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TestVoice(
        string channelId,
        [FromBody] TtsTestRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsTestResultDto> result = await _ttsConfigService.TestVoiceAsync(
            broadcasterId,
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<TtsTestResultDto> { Data = result.Value });
    }

    // ── Pronunciation lexicon (tts.md) — per-channel phrase → spoken-replacement rules ──────────────
    // Reuses the config action keys: reading the rules is a config read, changing them is a config write.

    /// <summary>All pronunciation rules for the channel (phrase → what TTS speaks instead).</summary>
    [HttpGet("lexicon")]
    [RequireAction("tts:config:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<TtsLexiconEntryDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetLexicon(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<IReadOnlyList<TtsLexiconEntryDto>> result = await _ttsLexiconService.ListAsync(
            broadcasterId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Add a pronunciation rule. A duplicate (phrase, match kind) is refused with 409-style ALREADY_EXISTS.</summary>
    [HttpPost("lexicon")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<TtsLexiconEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateLexiconEntry(
        string channelId,
        [FromBody] UpsertTtsLexiconEntryDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsLexiconEntryDto> result = await _ttsLexiconService.CreateAsync(
            broadcasterId,
            request,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Rewrite a pronunciation rule (phrase, replacement, and match kind).</summary>
    [HttpPut("lexicon/{entryId}")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<TtsLexiconEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLexiconEntry(
        string channelId,
        string entryId,
        [FromBody] UpsertTtsLexiconEntryDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(entryId, out Guid id))
            return BadRequestResponse("Invalid lexicon entry id.");
        Result<TtsLexiconEntryDto> result = await _ttsLexiconService.UpdateAsync(
            broadcasterId,
            id,
            request,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Remove a pronunciation rule.</summary>
    [HttpDelete("lexicon/{entryId}")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteLexiconEntry(
        string channelId,
        string entryId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(entryId, out Guid id))
            return BadRequestResponse("Invalid lexicon entry id.");
        Result result = await _ttsLexiconService.DeleteAsync(broadcasterId, id, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Bulk-import per-viewer voice assignments (≤500 rows) — the migration surface for another bot's
    /// user→voice table. Unknown Twitch users and uncatalogued voices come back as skipped rows with a
    /// reason. With <c>?createMissing=true</c>, an unknown Twitch user whose row carries a username is
    /// created as a bare viewer User first (the chat-ingest identity seam) so their legacy voice lands;
    /// without it nothing is ever created for them.
    /// </summary>
    [HttpPost("voices/assignments/import")]
    [RequireAction("tts:config:write")]
    [ProducesResponseType<StatusResponseDto<TtsVoiceImportResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ImportVoiceAssignments(
        string channelId,
        [FromBody] List<TtsVoiceAssignmentRowDto> request,
        CancellationToken ct,
        [FromQuery] bool createMissing = false
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<TtsVoiceImportResultDto> result =
            await _ttsConfigService.ImportUserVoiceAssignmentsAsync(
                broadcasterId,
                request,
                createMissing,
                ct
            );
        return ResultResponse(result);
    }

    /// <summary>Get a viewer's assigned TTS voice (404 when they use the channel default).</summary>
    [HttpGet("users/{userId}/voice")]
    [RequireAction("tts:voice:read")]
    [ProducesResponseType<StatusResponseDto<UserTtsVoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUserVoice(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<UserTtsVoiceDto> result = await _ttsConfigService.GetUserVoiceAsync(
            broadcasterId,
            userId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Assign a specific TTS voice to a viewer.</summary>
    [HttpPut("users/{userId}/voice")]
    [RequireAction("tts:uservoice:write")]
    [ProducesResponseType<StatusResponseDto<UserTtsVoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetUserVoice(
        string channelId,
        string userId,
        [FromBody] SetUserVoiceDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result<UserTtsVoiceDto> result = await _ttsConfigService.SetUserVoiceAsync(
            broadcasterId,
            userId,
            request,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Remove a viewer's voice assignment so they fall back to the channel default.</summary>
    [HttpDelete("users/{userId}/voice")]
    [RequireAction("tts:uservoice:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearUserVoice(
        string channelId,
        string userId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        Result result = await _ttsConfigService.ClearUserVoiceAsync(broadcasterId, userId, ct);
        return ResultResponse(result);
    }

    // ── Viewer self-service (the caller's OWN voice) ───────────────────────────
    // Self-scoped: [Authorize] only, no [RequireAction] — a viewer manages only their own voice. The service
    // enforces the channel's ViewerVoiceSelfServiceEnabled toggle (FEATURE_DISABLED → 403). The caller is a
    // dashboard User (JWT sub = internal User.Id), but a per-viewer voice keys on the PLATFORM external id the
    // dispatch resolver reads, so we map User.Id → the caller's identity under THIS channel's provider first.

    /// <summary>Get the caller's own assigned voice for this channel (Data is null when they use the channel default).</summary>
    [HttpGet("me/voice")]
    [ProducesResponseType<StatusResponseDto<UserTtsVoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOwnVoice(string channelId, CancellationToken ct)
    {
        CallerVoice caller = await ResolveCallerAsync(channelId, ct);
        if (caller.Error is not null)
            return caller.Error;

        Result<UserTtsVoiceDto?> result = await _ttsConfigService.GetOwnVoiceAsync(
            caller.BroadcasterId,
            caller.ExternalUserId!,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<UserTtsVoiceDto?> { Data = result.Value });
    }

    /// <summary>Pick the caller's own voice for this channel (toggle-gated; the voice must be one the channel can synthesize).</summary>
    [HttpPut("me/voice")]
    [ProducesResponseType<StatusResponseDto<UserTtsVoiceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetOwnVoice(
        string channelId,
        [FromBody] SetUserVoiceDto request,
        CancellationToken ct
    )
    {
        CallerVoice caller = await ResolveCallerAsync(channelId, ct);
        if (caller.Error is not null)
            return caller.Error;

        Result<UserTtsVoiceDto> result = await _ttsConfigService.SetOwnVoiceAsync(
            caller.BroadcasterId,
            caller.ExternalUserId!,
            request,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Reset the caller's own voice back to the channel default (toggle-gated).</summary>
    [HttpDelete("me/voice")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearOwnVoice(string channelId, CancellationToken ct)
    {
        CallerVoice caller = await ResolveCallerAsync(channelId, ct);
        if (caller.Error is not null)
            return caller.Error;

        Result result = await _ttsConfigService.ClearOwnVoiceAsync(
            caller.BroadcasterId,
            caller.ExternalUserId!,
            ct
        );
        return ResultResponse(result);
    }

    // Resolves the authenticated caller to their platform external id ON THIS CHANNEL'S provider — the id the TTS
    // dispatch resolver keys a per-viewer voice on. A caller with no linked identity under the channel's provider
    // has never spoken on this platform, so the self-service routes 404 ("connect your <provider> account") rather
    // than writing a voice row against an id that will never be dispatched.
    private async Task<CallerVoice> ResolveCallerAsync(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return CallerVoice.Fail(BadRequestResponse("Invalid channel id."));

        if (!Guid.TryParse(_currentUser.UserId, out Guid callerUserId))
            return CallerVoice.Fail(UnauthenticatedResponse());

        string? provider = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.Provider)
            .FirstOrDefaultAsync(ct);
        if (provider is null)
            return CallerVoice.Fail(NotFoundResponse("Channel not found."));

        string? externalUserId = await _db
            .UserIdentities.Where(i => i.UserId == callerUserId && i.Provider == provider)
            .Select(i => i.ProviderUserId)
            .FirstOrDefaultAsync(ct);
        if (externalUserId is null)
            return CallerVoice.Fail(
                NotFoundResponse($"Connect your {provider} account to pick a voice.")
            );

        return CallerVoice.Ok(broadcasterId, externalUserId);
    }

    // The outcome of resolving the caller to their on-provider external id: either the ids to act on, or the
    // short-circuit error response to return unchanged.
    private readonly record struct CallerVoice(
        Guid BroadcasterId,
        string? ExternalUserId,
        IActionResult? Error
    )
    {
        public static CallerVoice Ok(Guid broadcasterId, string externalUserId) =>
            new(broadcasterId, externalUserId, null);

        public static CallerVoice Fail(IActionResult error) => new(default, null, error);
    }
}
