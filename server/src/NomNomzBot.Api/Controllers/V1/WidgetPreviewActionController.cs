// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
/// Lets the widget editor preview run the ONE action a preview cannot fake: <c>tts_synthesize</c>. The preview
/// widget may not be saved yet, so this calls the owner action executor directly instead of the saved-widget
/// path the live overlay uses. Anything but the allow-listed action type is refused before it reaches the executor.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/widgets")]
[Authorize]
[Tags("Widgets")]
public sealed class WidgetPreviewActionController : BaseController
{
    /// <summary>The only action type a preview may run for real.</summary>
    public const string AllowedActionType = "tts_synthesize";

    private readonly IOwnerActionService _ownerActions;

    public WidgetPreviewActionController(IOwnerActionService ownerActions)
    {
        _ownerActions = ownerActions;
    }

    /// <summary>Run <c>tts_synthesize</c> for this channel and return the variables it set (<c>tts.audioUrl</c>,
    /// <c>tts.durationMs</c>). A failed run is a 200 with <c>success: false</c> and an error code, the same shape
    /// the overlay hub's invoke answers, so the preview SDK passes it through unchanged.</summary>
    [RequireAction("widget:write")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [HttpPost("preview-action")]
    [ProducesResponseType<StatusResponseDto<WidgetPreviewActionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Run(
        string channelId,
        [FromBody] WidgetPreviewActionRequest request,
        CancellationToken ct
    )
    {
        if (
            !string.Equals(
                request.ActionType,
                AllowedActionType,
                StringComparison.OrdinalIgnoreCase
            )
        )
            return BadRequestResponse(
                $"The preview can only run '{AllowedActionType}'.",
                "ACTION_NOT_ALLOWED"
            );

        if (
            !GuidUlidCodec.TryDecode(channelId, out Guid broadcasterId)
            && !Guid.TryParse(channelId, out broadcasterId)
        )
            return BadRequestResponse("Invalid channel id.");

        Result<WidgetActionOutcome> run = await _ownerActions.RunAsync(
            new OwnerActionRequest(
                broadcasterId,
                AllowedActionType,
                request.Parameters,
                request.Variables,
                "widget preview",
                $"widget-preview:{broadcasterId}"
            ),
            ct
        );

        WidgetPreviewActionResponse response = run.IsSuccess
            ? new(run.Value.Succeeded, run.Value.Output, run.Value.Error, null, run.Value.Variables)
            : new(false, null, run.ErrorMessage, run.ErrorCode, new Dictionary<string, string>());
        return Ok(new StatusResponseDto<WidgetPreviewActionResponse> { Data = response });
    }
}

/// <summary>A preview action request: the action type, its parameters and the variables that seed its context.</summary>
public sealed record WidgetPreviewActionRequest(
    string ActionType,
    Dictionary<string, JsonElement>? Parameters,
    Dictionary<string, string>? Variables
);

/// <summary>What the preview action did; the same fields the overlay hub's invoke returns.</summary>
public sealed record WidgetPreviewActionResponse(
    bool Success,
    string? Output,
    string? Error,
    string? ErrorCode,
    IReadOnlyDictionary<string, string> Variables
);
