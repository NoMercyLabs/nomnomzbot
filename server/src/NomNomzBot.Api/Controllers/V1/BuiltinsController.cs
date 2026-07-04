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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Manages the channel's built-in commands: listing and per-command enable/configure.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/builtins")]
[Authorize]
[Tags("Commands")]
public sealed class BuiltinsController : BaseController
{
    private readonly IBuiltinCommandService _builtins;

    public BuiltinsController(IBuiltinCommandService builtins)
    {
        _builtins = builtins;
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
}

public sealed record SetBuiltinEnabledRequest(bool Enabled);
