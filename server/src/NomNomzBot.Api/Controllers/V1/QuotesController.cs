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
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The channel's numbered quote library (quotes.md §5). Gate 1 is <c>[Authorize]</c> + tenant resolution from
/// the JWT (<see cref="ICurrentTenantService"/>) — quotes are inherently "my own channel". Gate 2 is the
/// per-route <c>[RequireAction]</c> floor: reading + adding/editing (<c>quotes:read</c> / <c>quotes:write</c>)
/// sit at VIP so a trusted VIP can curate quotes; deleting (<c>quotes:delete</c>) stays Moderator.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/quotes")]
[Authorize]
[Tags("Quotes")]
public class QuotesController : BaseController
{
    private readonly IQuoteService _quotes;
    private readonly ICurrentTenantService _tenant;

    public QuotesController(IQuoteService quotes, ICurrentTenantService tenant)
    {
        _quotes = quotes;
        _tenant = tenant;
    }

    /// <summary>List the channel's quotes with optional text search, paginated.</summary>
    [RequireAction("quotes:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<QuoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListQuotes(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        QuoteSearch search = new(request.Search);
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<QuoteDto>> result = await _quotes.ListAsync(
            broadcasterId,
            search,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Read a random quote from the channel's library.</summary>
    [RequireAction("quotes:read")]
    [HttpGet("random")]
    [ProducesResponseType<StatusResponseDto<QuoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRandomQuote(CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<QuoteDto> result = await _quotes.GetRandomAsync(broadcasterId, ct);
        return ResultResponse(result);
    }

    /// <summary>Read a single quote by its channel-scoped number.</summary>
    [RequireAction("quotes:read")]
    [HttpGet("{number:int}")]
    [ProducesResponseType<StatusResponseDto<QuoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuote(int number, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<QuoteDto> result = await _quotes.GetAsync(broadcasterId, number, ct);
        return ResultResponse(result);
    }

    /// <summary>Add a new quote, returning 201 with its assigned number.</summary>
    [RequireAction("quotes:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<QuoteDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddQuote(
        [FromBody] AddQuoteRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<QuoteDto> result = await _quotes.AddAsync(broadcasterId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetQuote),
            new { number = result.Value.Number },
            new StatusResponseDto<QuoteDto>
            {
                Data = result.Value,
                Message = $"Quote #{result.Value.Number} added.",
            }
        );
    }

    /// <summary>Edit an existing quote by its number.</summary>
    [RequireAction("quotes:write")]
    [HttpPut("{number:int}")]
    [ProducesResponseType<StatusResponseDto<QuoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EditQuote(
        int number,
        [FromBody] EditQuoteRequest request,
        CancellationToken ct
    )
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result<QuoteDto> result = await _quotes.EditAsync(broadcasterId, number, request, ct);
        return ResultResponse(result);
    }

    /// <summary>Delete a quote by its number.</summary>
    [RequireAction("quotes:delete")]
    [HttpDelete("{number:int}")]
    [ProducesResponseType<StatusResponseDto<QuoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteQuote(int number, CancellationToken ct)
    {
        if (_tenant.BroadcasterId is not Guid broadcasterId)
            return UnauthenticatedResponse("No tenant resolved.");

        Result result = await _quotes.DeleteAsync(broadcasterId, number, ct);
        return ResultResponse(result);
    }
}
