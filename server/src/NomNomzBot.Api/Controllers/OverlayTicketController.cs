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
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Exchanges an overlay browser-source token for a short-lived, single-use ticket (S035 item 3, U·B5/B7). The
/// overlay SDK sends the token in a header here — a plain HTTP request CAN carry custom headers, unlike the
/// WebSocket upgrade OBS browser sources use — and only the resulting ticket ever appears on the
/// <c>/hubs/overlay</c> query string. The token resolves via <see cref="IWidgetService.ResolveBroadcasterIdByOverlayTokenAsync"/>
/// — a widget's own <c>OverlayToken</c> (audit B5, the per-widget page's normal case), its still-live
/// <c>PreviousOverlayToken</c> during a rotation grace window, or (legacy) the channel-wide <c>Channel.OverlayToken</c>.
/// Anonymous (the token itself is the credential) but throttled per token so a leaked token, or a runaway
/// source, cannot hammer this endpoint unbounded.
/// </summary>
[ApiController]
[Route("overlay")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(RateLimiting.RateLimitPolicyNames.Anonymous)]
public sealed class OverlayTicketController : ControllerBase
{
    private const string TokenHeaderName = "X-Overlay-Token";

    private readonly IWidgetService _widgetService;
    private readonly IOverlayTicketService _tickets;
    private readonly IOverlayConnectionThrottle _throttle;

    public OverlayTicketController(
        IWidgetService widgetService,
        IOverlayTicketService tickets,
        IOverlayConnectionThrottle throttle
    )
    {
        _widgetService = widgetService;
        _tickets = tickets;
        _throttle = throttle;
    }

    [HttpPost("ticket")]
    public async Task<IActionResult> IssueTicket(CancellationToken cancellationToken)
    {
        string? token = Request.Headers[TokenHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(token))
            return Unauthorized();

        if (!_throttle.TryAcquire(token))
            return StatusCode(StatusCodes.Status429TooManyRequests);

        Guid? broadcasterId = await _widgetService.ResolveBroadcasterIdByOverlayTokenAsync(
            token,
            cancellationToken
        );
        if (broadcasterId is null)
            return Unauthorized();

        string ticket = _tickets.IssueTicket(broadcasterId.Value);
        return Ok(new { ticket });
    }
}
