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
    IOutboundWebhookEndpointService outbound,
    NomNomzBot.Application.Abstractions.Auth.ICurrentUserService currentUser
) : BaseController
{
    /// <summary>List the channel's inbound webhook endpoints, paginated.</summary>
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

    /// <summary>Fetch a single inbound webhook endpoint by id.</summary>
    [HttpGet("inbound/{endpointId:guid}")]
    [RequireAction("webhooks:inbound:read")]
    public async Task<IActionResult> GetInbound(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await inbound.GetAsync(channelId, endpointId, ct));

    /// <summary>Create an inbound webhook endpoint for the channel, recording the caller as its creator.</summary>
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

    /// <summary>Update an inbound webhook endpoint's configuration.</summary>
    [HttpPut("inbound/{endpointId:guid}")]
    [RequireAction("webhooks:inbound:write")]
    public async Task<IActionResult> UpdateInbound(
        Guid channelId,
        Guid endpointId,
        [FromBody] UpdateInboundWebhookRequest request,
        CancellationToken ct
    ) => ResultResponse(await inbound.UpdateAsync(channelId, endpointId, request, ct));

    /// <summary>Rotate the opaque URL token for an inbound webhook endpoint, invalidating the old ingest URL.</summary>
    [HttpPost("inbound/{endpointId:guid}/rotate-token")]
    [RequireAction("webhooks:inbound:write")]
    public async Task<IActionResult> RotateInboundToken(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await inbound.RotateTokenAsync(channelId, endpointId, ct));

    /// <summary>Delete an inbound webhook endpoint, retiring its ingest URL.</summary>
    [HttpDelete("inbound/{endpointId:guid}")]
    [RequireAction("webhooks:inbound:write")]
    public async Task<IActionResult> DeleteInbound(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await inbound.DeleteAsync(channelId, endpointId, ct));

    /// <summary>List the channel's outbound webhook endpoints, paginated.</summary>
    [HttpGet("outbound")]
    [RequireAction("webhooks:outbound:read")]
    [ProducesResponseType<PaginatedResponse<OutboundWebhookEndpointDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOutbound(
        Guid channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<OutboundWebhookEndpointDto>> result = await outbound.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Fetch a single outbound webhook endpoint by id.</summary>
    [HttpGet("outbound/{endpointId:guid}")]
    [RequireAction("webhooks:outbound:read")]
    public async Task<IActionResult> GetOutbound(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await outbound.GetAsync(channelId, endpointId, ct));

    /// <summary>Create an outbound webhook endpoint for the channel, recording the caller as its creator.</summary>
    [HttpPost("outbound")]
    [RequireAction("webhooks:outbound:write")]
    public async Task<IActionResult> CreateOutbound(
        Guid channelId,
        [FromBody] CreateOutboundWebhookRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await outbound.CreateAsync(channelId, caller, request, ct));
    }

    /// <summary>Update an outbound webhook endpoint's configuration.</summary>
    [HttpPut("outbound/{endpointId:guid}")]
    [RequireAction("webhooks:outbound:write")]
    public async Task<IActionResult> UpdateOutbound(
        Guid channelId,
        Guid endpointId,
        [FromBody] UpdateOutboundWebhookRequest request,
        CancellationToken ct
    ) => ResultResponse(await outbound.UpdateAsync(channelId, endpointId, request, ct));

    /// <summary>Rotate the shared secret used to sign an outbound webhook endpoint's deliveries.</summary>
    [HttpPost("outbound/{endpointId:guid}/rotate-secret")]
    [RequireAction("webhooks:outbound:write")]
    public async Task<IActionResult> RotateOutboundSecret(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await outbound.RotateSecretAsync(channelId, endpointId, ct));

    /// <summary>Re-enable an outbound webhook endpoint that was disabled after delivery failures.</summary>
    [HttpPost("outbound/{endpointId:guid}/reenable")]
    [RequireAction("webhooks:outbound:write")]
    public async Task<IActionResult> ReenableOutbound(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await outbound.ReenableAsync(channelId, endpointId, ct));

    /// <summary>Send a test delivery to an outbound webhook endpoint.</summary>
    [HttpPost("outbound/{endpointId:guid}/test")]
    [RequireAction("webhooks:outbound:write")]
    public async Task<IActionResult> TestOutbound(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await outbound.SendTestAsync(channelId, endpointId, ct));

    /// <summary>Delete an outbound webhook endpoint, stopping its deliveries.</summary>
    [HttpDelete("outbound/{endpointId:guid}")]
    [RequireAction("webhooks:outbound:write")]
    public async Task<IActionResult> DeleteOutbound(
        Guid channelId,
        Guid endpointId,
        CancellationToken ct
    ) => ResultResponse(await outbound.DeleteAsync(channelId, endpointId, ct));

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
