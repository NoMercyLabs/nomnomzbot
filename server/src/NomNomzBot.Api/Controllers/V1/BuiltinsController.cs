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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages the channel's built-in commands: listing, per-command enable/configure, and every reply's wording.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/builtins")]
[Authorize]
[Tags("Commands")]
public sealed class BuiltinsController : BaseController
{
    private readonly IBuiltinCommandService _builtins;
    private readonly IBuiltinReplyService _replies;

    public BuiltinsController(IBuiltinCommandService builtins, IBuiltinReplyService replies)
    {
        _builtins = builtins;
        _replies = replies;
    }

    /// <summary>
    /// Lists all built-in commands for the channel, with their enabled state and defaults.
    /// </summary>
    [RequireAction("commands:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<BuiltinCommandDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListBuiltins(string channelId, CancellationToken ct)
    {
        Result<IReadOnlyList<BuiltinCommandDto>> result = await _builtins.ListAsync(channelId, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// One built-in as the channel sees it: its defaults, the channel's own cooldown / permission / TTS settings,
    /// and how many reworded replies a reset would put back.
    /// </summary>
    [RequireAction("commands:read")]
    [HttpGet("{builtinKey}")]
    [ProducesResponseType<StatusResponseDto<BuiltinCommandDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBuiltin(
        string channelId,
        string builtinKey,
        CancellationToken ct
    )
    {
        Result<BuiltinCommandDto> result = await _builtins.GetAsync(channelId, builtinKey, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Sets the channel's cooldown (0–3600 s) and permission floor for a built-in. A null field, or a value equal
    /// to the default, inherits the default. The floor can be raised but never lowered below the built-in's own
    /// (400); reserved data-rights built-ins cannot be changed (400).
    /// </summary>
    [RequireAction("commands:write")]
    [HttpPut("{builtinKey}/settings")]
    [ProducesResponseType<StatusResponseDto<BuiltinCommandDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSettings(
        string channelId,
        string builtinKey,
        [FromBody] UpdateBuiltinSettingsRequest body,
        CancellationToken ct
    )
    {
        Result<BuiltinCommandDto> result = await _builtins.UpdateSettingsAsync(
            channelId,
            builtinKey,
            new BuiltinSettingsUpdate(body.CooldownSeconds, body.MinPermissionLevel),
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Resets a built-in to its defaults for the channel: enabled, TTS off, default cooldown and permission, and
    /// the default wording for every reply it speaks with. Returns the built-in as it now resolves.
    /// </summary>
    [NotDestructive(
        "Clears one built-in's per-channel settings (OverridesJson + the enabled flag); the catalogue defaults take over, nothing references the cleared values, and every setting can be set again at any time. The dashboard shows what will be reset before it confirms."
    )]
    [RequireAction("commands:write")]
    [HttpDelete("{builtinKey}/settings")]
    [ProducesResponseType<StatusResponseDto<BuiltinCommandDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetBuiltin(
        string channelId,
        string builtinKey,
        CancellationToken ct
    )
    {
        Result<BuiltinCommandDto> result = await _builtins.ResetAsync(channelId, builtinKey, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Enables or disables a specific built-in command for the channel.
    /// </summary>
    [RequireAction("commands:write")]
    [HttpPatch("{builtinKey}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetEnabled(
        string channelId,
        string builtinKey,
        [FromBody] SetBuiltinEnabledRequest body,
        CancellationToken ct
    )
    {
        Result result = await _builtins.SetEnabledAsync(channelId, builtinKey, body.Enabled, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// The channel's built-in reply catalogue (commands-pipelines.md §11): every reply slot of every built-in,
    /// what it says for this channel right now, which layer that text comes from (channel / platform / tone),
    /// the default it falls back to, and the variables it can use.
    /// </summary>
    [RequireAction("commands:read")]
    [HttpGet("replies")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<BuiltinReplyGroupDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListReplies(string channelId, CancellationToken ct)
    {
        Result<IReadOnlyList<BuiltinReplyGroupDto>> result = await _replies.ListAsync(
            channelId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Sets the channel's own text for exactly one reply slot. A blank template resets the slot. An unknown
    /// variable, a locked data-rights reply, or text over 500 characters is rejected (400).
    /// </summary>
    [RequireAction("commands:write")]
    [HttpPut("{builtinKey}/replies/{slot}")]
    [ProducesResponseType<StatusResponseDto<BuiltinReplyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetReply(
        string channelId,
        string builtinKey,
        string slot,
        [FromBody] SetBuiltinReplyRequest body,
        CancellationToken ct
    )
    {
        Result<BuiltinReplyDto> result = await _replies.SetAsync(
            channelId,
            builtinKey,
            slot,
            body.Template,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>Resets one reply slot to its default; returns the slot as it now resolves.</summary>
    [NotDestructive(
        "Removes one per-slot template from the built-in's OverridesJson; nothing references it, the reply falls back to the platform/tone default, and it can be set again at any time."
    )]
    [RequireAction("commands:write")]
    [HttpDelete("{builtinKey}/replies/{slot}")]
    [ProducesResponseType<StatusResponseDto<BuiltinReplyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetReply(
        string channelId,
        string builtinKey,
        string slot,
        CancellationToken ct
    )
    {
        Result<BuiltinReplyDto> result = await _replies.ResetAsync(channelId, builtinKey, slot, ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Enables or disables the channel's "speak with TTS" option for a built-in that supports it (S-OBS-12,
    /// e.g. <c>!quote</c>) — default off. When on, the built-in also queues its reply text through the TTS
    /// service; when off, nothing reaches TTS.
    /// </summary>
    [RequireAction("commands:write")]
    [HttpPut("{builtinKey}/tts")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSpeakWithTts(
        string channelId,
        string builtinKey,
        [FromBody] SetBuiltinSpeakWithTtsRequest body,
        CancellationToken ct
    )
    {
        Result result = await _builtins.SetSpeakWithTtsAsync(
            channelId,
            builtinKey,
            body.Enabled,
            ct
        );
        return ResultResponse(result);
    }
}

public sealed record SetBuiltinEnabledRequest(bool Enabled);

public sealed record SetBuiltinReplyRequest(string? Template);

public sealed record SetBuiltinSpeakWithTtsRequest(bool Enabled);

/// <summary>The channel's cooldown and permission floor for a built-in; null = use the default.</summary>
public sealed record UpdateBuiltinSettingsRequest(int? CooldownSeconds, string? MinPermissionLevel);
