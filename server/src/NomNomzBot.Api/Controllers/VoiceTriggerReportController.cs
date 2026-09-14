// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// The voice-listener page's report call (voice-triggers feature). The listener is conceptually "just
/// another overlay client" for auth purposes: it authenticates with the SAME <c>X-Overlay-Token</c> header
/// mechanism as <see cref="OverlayTicketController"/>, resolved via
/// <see cref="IWidgetService.ResolveBroadcasterIdByOverlayTokenAsync"/> — no new auth scheme. Unlike the
/// SignalR-bound ticket exchange, a plain POST needs no short-lived ticket hop: the long-lived token rides
/// directly in the header on every report (a header, never a query string or WS URL).
/// </summary>
[ApiController]
[Route("overlay/voice-trigger")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(NomNomzBot.Api.RateLimiting.RateLimitPolicyNames.Anonymous)]
public sealed class VoiceTriggerReportController : ControllerBase
{
    private const string TokenHeaderName = "X-Overlay-Token";

    private readonly IWidgetService _widgetService;
    private readonly IVoiceTriggerService _triggers;

    public VoiceTriggerReportController(IWidgetService widgetService, IVoiceTriggerService triggers)
    {
        _widgetService = widgetService;
        _triggers = triggers;
    }

    /// <summary>Report a heard transcript; the server decides which (if any) trigger fired.</summary>
    [HttpPost("report")]
    [ProducesResponseType<VoiceTriggerReportResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(
        [FromBody] ReportVoiceTriggerRequest request,
        CancellationToken ct
    )
    {
        string? token = Request.Headers[TokenHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(token))
            return Unauthorized();

        Guid? broadcasterId = await _widgetService.ResolveBroadcasterIdByOverlayTokenAsync(
            token,
            ct
        );
        if (broadcasterId is null)
            return Unauthorized();

        Result<VoiceTriggerReportResultDto> result = await _triggers.ReportAsync(
            broadcasterId.Value,
            request.Transcript,
            ct
        );
        if (result.IsFailure)
            return BadRequest(new { result.ErrorMessage });

        return Ok(result.Value);
    }
}
