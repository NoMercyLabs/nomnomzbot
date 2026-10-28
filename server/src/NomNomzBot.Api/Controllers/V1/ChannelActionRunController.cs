// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Runs any registered pipeline action (including <c>run_pipeline</c>) from the dashboard, so any button
/// anywhere can trigger it. The caller needs <c>pipelines:write</c> in the channel, the same permission the
/// pipeline test-run asks for; the executor then checks the owner's own permission again.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/actions")]
[Authorize]
[Tags("Actions")]
public sealed class ChannelActionRunController(IOwnerActionService ownerActions) : BaseController
{
    /// <summary>Run one action. A run that starts and fails is a 200 with <c>success: false</c>; an unknown
    /// action type is a 404 and a caller without the permission a 403.</summary>
    [RequireAction("pipelines:write")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [HttpPost("run")]
    [ProducesResponseType<StatusResponseDto<ChannelActionRunResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Run(
        string channelId,
        [FromBody] ChannelActionRunRequest request,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.ActionType))
            return BadRequestResponse("Name the action to run.", "VALIDATION_FAILED");

        if (
            !GuidUlidCodec.TryDecode(channelId, out Guid broadcasterId)
            && !Guid.TryParse(channelId, out broadcasterId)
        )
            return BadRequestResponse("Invalid channel id.");

        Result<WidgetActionOutcome> run = await ownerActions.RunAsync(
            new OwnerActionRequest(
                broadcasterId,
                request.ActionType.Trim(),
                request.Parameters,
                request.Variables,
                "dashboard",
                $"dashboard-action:{broadcasterId}"
            ),
            ct
        );
        if (run.IsFailure)
            return ResultResponse(run);

        WidgetActionOutcome outcome = run.Value;
        return Ok(
            new StatusResponseDto<ChannelActionRunResponse>
            {
                Data = new(outcome.Succeeded, outcome.Output, outcome.Error, outcome.Variables),
            }
        );
    }
}

/// <summary>An action to run: its type, its parameters and the variables that seed its context (for example
/// <c>target.id</c> and <c>target.name</c> for a user, <c>song.name</c> for a track).</summary>
public sealed record ChannelActionRunRequest(
    string ActionType,
    Dictionary<string, JsonElement>? Parameters,
    Dictionary<string, string>? Variables
);

/// <summary>What the action did: whether it succeeded, its output text, its error and the variables it set.</summary>
public sealed record ChannelActionRunResponse(
    bool Success,
    string? Output,
    string? Error,
    IReadOnlyDictionary<string, string> Variables
);
