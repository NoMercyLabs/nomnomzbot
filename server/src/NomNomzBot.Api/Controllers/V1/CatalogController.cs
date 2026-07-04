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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The store catalog + redemptions (economy.md §5). Item CRUD is management-floored; a purchase is community
/// (the item's own <c>Permission</c> is enforced in the service against the caller's resolved level — which the
/// controller resolves server-side, never trusting the body for the buyer identity or level).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/economy/catalog")]
[Authorize]
[Tags("Economy — Catalog")]
public class CatalogController(
    ICatalogService catalog,
    IRoleResolver roles,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>List the channel's catalog items, paginated.</summary>
    [HttpGet]
    [RequireAction("economy:catalog:read")]
    [ProducesResponseType<PaginatedResponse<CatalogItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListItems(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<CatalogItemDto>> result = await catalog.ListItemsAsync(
            broadcasterId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Read a single catalog item by id.</summary>
    [HttpGet("{itemId:guid}")]
    [RequireAction("economy:catalog:read")]
    public async Task<IActionResult> GetItem(string channelId, Guid itemId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await catalog.GetItemAsync(broadcasterId, itemId, ct));
    }

    /// <summary>Create a new catalog item for the channel.</summary>
    [HttpPost]
    [RequireAction("economy:catalog:create")]
    public async Task<IActionResult> CreateItem(
        string channelId,
        [FromBody] CreateCatalogItemRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await catalog.CreateItemAsync(broadcasterId, request, ct));
    }

    /// <summary>Partially update an existing catalog item.</summary>
    [HttpPatch("{itemId:guid}")]
    [RequireAction("economy:catalog:update")]
    public async Task<IActionResult> UpdateItem(
        string channelId,
        Guid itemId,
        [FromBody] UpdateCatalogItemRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await catalog.UpdateItemAsync(broadcasterId, itemId, request, ct));
    }

    /// <summary>Delete a catalog item from the channel's store.</summary>
    [HttpDelete("{itemId:guid}")]
    [RequireAction("economy:catalog:delete")]
    public async Task<IActionResult> DeleteItem(string channelId, Guid itemId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await catalog.DeleteItemAsync(broadcasterId, itemId, ct));
    }

    /// <summary>Purchase a catalog item as the authenticated caller, with the buyer's role level resolved server-side.</summary>
    [HttpPost("{itemId:guid}/purchase")]
    [RequireAction("economy:catalog:purchase")]
    public async Task<IActionResult> Purchase(
        string channelId,
        Guid itemId,
        [FromBody] PurchaseRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        // The buyer is the caller; the role level is resolved server-side — the body controls neither.
        Result<int> level = await roles.ResolveEffectiveLevelAsync(caller, broadcasterId, ct);
        if (level.IsFailure)
            return ResultResponse(level);
        PurchaseRequest bound = request with
        {
            ItemId = itemId,
            BuyerUserId = caller,
            RoleLevel = level.Value,
        };
        return ResultResponse(await catalog.PurchaseAsync(broadcasterId, bound, ct));
    }

    /// <summary>List the channel's catalog purchases, filtered and paginated.</summary>
    [HttpGet("purchases")]
    [RequireAction("economy:catalog:purchases:read")]
    [ProducesResponseType<PaginatedResponse<CatalogPurchaseDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPurchases(
        string channelId,
        [FromQuery] PurchaseFilter filter,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<CatalogPurchaseDto>> result = await catalog.ListPurchasesAsync(
            broadcasterId,
            filter,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Refund a purchase, binding the acting refunder to the authenticated caller.</summary>
    [HttpPost("purchases/{purchaseId:long}/refund")]
    [RequireAction("economy:catalog:refund")]
    public async Task<IActionResult> Refund(
        string channelId,
        long purchaseId,
        [FromBody] RefundRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        RefundRequest bound = request with { ActorUserId = caller };
        return ResultResponse(
            await catalog.RefundPurchaseAsync(broadcasterId, purchaseId, bound, ct)
        );
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
