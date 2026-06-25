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
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Currency configuration, wallets, the ledger, and transfers (economy.md §5). Gate 1 = <c>[Authorize]</c> +
/// tenant resolution (channel access); Gate 2 = the per-route <c>[RequireAction]</c> floor.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/economy")]
[Authorize]
[Tags("Economy — Currency")]
public class CurrencyController(
    ICurrencyConfigService config,
    ICurrencyAccountService accounts,
    ICurrentUserService currentUser
) : BaseController
{
    public record FreezeBody(bool Frozen);

    [HttpGet("config")]
    [RequireAction("economy:config:read")]
    public async Task<IActionResult> GetConfig(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await config.GetConfigAsync(broadcasterId, ct));
    }

    [HttpPut("config")]
    [RequireAction("economy:config:write")]
    public async Task<IActionResult> UpsertConfig(
        string channelId,
        [FromBody] UpsertCurrencyConfigRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await config.UpsertConfigAsync(broadcasterId, request, ct));
    }

    [HttpGet("earning-rules")]
    [RequireAction("economy:earning-rules:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<EarningRuleDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListEarningRules(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await config.ListEarningRulesAsync(broadcasterId, ct));
    }

    [HttpPut("earning-rules")]
    [RequireAction("economy:earning-rules:write")]
    public async Task<IActionResult> UpsertEarningRule(
        string channelId,
        [FromBody] UpsertEarningRuleRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await config.UpsertEarningRuleAsync(broadcasterId, request, ct));
    }

    [HttpDelete("earning-rules/{ruleId:guid}")]
    [RequireAction("economy:earning-rules:delete")]
    public async Task<IActionResult> DeleteEarningRule(
        string channelId,
        Guid ruleId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await config.DeleteEarningRuleAsync(broadcasterId, ruleId, ct));
    }

    [HttpGet("accounts")]
    [RequireAction("economy:accounts:read")]
    [ProducesResponseType<PaginatedResponse<CurrencyAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAccounts(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<CurrencyAccountDto>> result = await accounts.ListAccountsAsync(
            broadcasterId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>
    /// The caller's OWN wallet on this channel — the <c>self</c> arm of economy.md §5's
    /// <c>economy:account:read</c> "self-or-Gate-2". It binds the subject to the authenticated caller and can
    /// only ever return that caller's account, so it carries no Gate-2 floor (community / Everyone): a
    /// participant reads their own balance here without the Moderator <c>economy:account:read</c> the keyed
    /// <c>accounts/{viewerUserId}</c> route demands. <c>me</c> is a literal, so it never shadows the guid route.
    /// </summary>
    [HttpGet("accounts/me")]
    [ProducesResponseType<StatusResponseDto<CurrencyAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyAccount(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await accounts.GetOrCreateAccountAsync(broadcasterId, caller, ct));
    }

    [HttpGet("accounts/{viewerUserId:guid}")]
    [RequireAction("economy:account:read")]
    public async Task<IActionResult> GetAccount(
        string channelId,
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(
            await accounts.GetOrCreateAccountAsync(broadcasterId, viewerUserId, ct)
        );
    }

    [HttpGet("accounts/{viewerUserId:guid}/ledger")]
    [RequireAction("economy:ledger:read")]
    [ProducesResponseType<PaginatedResponse<CurrencyLedgerEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLedger(
        string channelId,
        Guid viewerUserId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<CurrencyLedgerEntryDto>> result = await accounts.GetLedgerAsync(
            broadcasterId,
            viewerUserId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpPost("accounts/{viewerUserId:guid}/adjust")]
    [RequireAction("economy:account:adjust")]
    public async Task<IActionResult> Adjust(
        string channelId,
        Guid viewerUserId,
        [FromBody] AdminAdjustCommand command,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        // Bind the subject to the route and the actor to the caller — never trust the body for either.
        AdminAdjustCommand bound = command with
        {
            ViewerUserId = viewerUserId,
            ActorUserId = caller,
        };
        return ResultResponse(await accounts.AdminAdjustAsync(broadcasterId, bound, ct));
    }

    [HttpPost("accounts/{viewerUserId:guid}/freeze")]
    [RequireAction("economy:account:freeze")]
    public async Task<IActionResult> Freeze(
        string channelId,
        Guid viewerUserId,
        [FromBody] FreezeBody body,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(
            await accounts.SetFrozenAsync(broadcasterId, viewerUserId, body.Frozen, ct)
        );
    }

    [HttpPost("transfer")]
    [RequireAction("economy:transfer:write")]
    public async Task<IActionResult> Transfer(
        string channelId,
        [FromBody] TransferCommand command,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        TransferCommand bound = command with { ActorUserId = caller };
        return ResultResponse(await accounts.TransferAsync(broadcasterId, bound, ct));
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
