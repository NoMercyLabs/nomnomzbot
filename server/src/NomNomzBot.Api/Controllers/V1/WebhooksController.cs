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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Webhook configuration — management plane (webhooks.md §5.1). Sensitive egress + secrets, so reads are
/// Moderator-floored and writes Editor-floored. The acting user is bound from the caller. (Outbound endpoint
/// management is deferred with the H.7 egress-allowlist dependency; this controller serves the inbound surface.)
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/webhooks")]
[Authorize]
[Tags("Webhooks")]
public class WebhooksController(
    IInboundWebhookEndpointService inbound,
    NomNomzBot.Application.Abstractions.Auth.ICurrentUserService currentUser
) : BaseController
{
    [HttpGet("inbound")]
    [RequireAction("webhooks:inbound:read")]
    [ProducesResponseType<PaginatedResponse<InboundWebhookEndpointDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInbound(
        Guid channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<InboundWebhookEndpointDto>> result = await inbound.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpGet("inbound/{endpointId:guid}")]
    [RequireAction("webhooks:inbound:read")]
    public async Task<IActionResult> GetInbound(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await inbound.GetAsync(channelId, endpointId, ct));

    [HttpPost("inbound")]
    [RequireAction("webhooks:inbound:write")]
    public async Task<IActionResult> CreateInbound(
        Guid channelId,
        [FromBody] CreateInboundWebhookRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await inbound.CreateAsync(channelId, caller, request, ct));
    }

    [HttpPut("inbound/{endpointId:guid}")]
    [RequireAction("webhooks:inbound:write")]
    public async Task<IActionResult> UpdateInbound(
        Guid channelId,
        Guid endpointId,
        [FromBody] UpdateInboundWebhookRequest request,
        CancellationToken ct
    ) => ResultResponse(await inbound.UpdateAsync(channelId, endpointId, request, ct));

    [HttpPost("inbound/{endpointId:guid}/rotate-token")]
    [RequireAction("webhooks:inbound:write")]
    public async Task<IActionResult> RotateInboundToken(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await inbound.RotateTokenAsync(channelId, endpointId, ct));

    [HttpDelete("inbound/{endpointId:guid}")]
    [RequireAction("webhooks:inbound:write")]
    public async Task<IActionResult> DeleteInbound(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await inbound.DeleteAsync(channelId, endpointId, ct));

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
