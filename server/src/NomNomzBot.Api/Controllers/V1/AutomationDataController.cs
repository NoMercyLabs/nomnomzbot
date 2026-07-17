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
using NomNomzBot.Api.Authentication;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The automation data plane (automation-api.md §4.1) — the surface third-party tools call with a
/// channel's API token (<c>Authorization: Bearer nnzb_ak_…</c>), NOT a dashboard JWT. Scopes and the
/// per-token rate limits are enforced in the command service; a limited call answers 429 with
/// <c>Retry-After</c>. No Gate-2 here by design — the token's own scopes are the authorization.
/// </summary>
[ApiVersionNeutral]
[Route("automation/v1")]
[Authorize(AuthenticationSchemes = ApiTokenAuthenticationHandler.SchemeName)]
[Tags("Automation data plane")]
public class AutomationDataController(IAutomationCommandService commands) : BaseController
{
    /// <summary>Broadcaster + instance summary (scope <c>read</c>).</summary>
    [HttpGet("info")]
    [ProducesResponseType<StatusResponseDto<AutomationInfo>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInfo(CancellationToken ct)
    {
        if (Principal is not AutomationPrincipal principal)
            return UnauthenticatedResponse();
        return WithRetryAfter(await commands.GetInfoAsync(principal, ct));
    }

    /// <summary>Invocable pipelines (scope <c>read</c>; honors the token's allowlist).</summary>
    [HttpGet("pipelines")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<AutomationPipelineRef>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListPipelines(CancellationToken ct)
    {
        if (Principal is not AutomationPrincipal principal)
            return UnauthenticatedResponse();
        return WithRetryAfter(await commands.ListPipelinesAsync(principal, ct));
    }

    /// <summary>The channel's enabled chat commands (scope <c>read</c>).</summary>
    [HttpGet("commands")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<AutomationCommandRef>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListCommands(CancellationToken ct)
    {
        if (Principal is not AutomationPrincipal principal)
            return UnauthenticatedResponse();
        return WithRetryAfter(await commands.ListCommandsAsync(principal, ct));
    }

    /// <summary>Run a pipeline fire-and-forget (scope <c>invoke</c> + allowlist).</summary>
    [HttpPost("invoke")]
    [ProducesResponseType<StatusResponseDto<AutomationInvokeResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(
        [FromBody] AutomationInvokeRequest request,
        CancellationToken ct
    )
    {
        if (Principal is not AutomationPrincipal principal)
            return UnauthenticatedResponse();
        return WithRetryAfter(await commands.InvokePipelineAsync(principal, request, ct));
    }

    /// <summary>Send a chat message / reply / whisper as the bot (scope <c>chat</c>).</summary>
    [HttpPost("chat")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendChat(
        [FromBody] AutomationChatRequest request,
        CancellationToken ct
    )
    {
        if (Principal is not AutomationPrincipal principal)
            return UnauthenticatedResponse();
        Result result = await commands.SendChatAsync(principal, request, ct);
        SetRetryAfter(result.ErrorCode, result.ErrorDetail);
        return ResultResponse(result);
    }

    /// <summary>The principal the authentication handler parked for this request.</summary>
    private AutomationPrincipal? Principal =>
        HttpContext.Items[typeof(AutomationPrincipal)] as AutomationPrincipal;

    private IActionResult WithRetryAfter<T>(Result<T> result)
    {
        SetRetryAfter(result.ErrorCode, result.ErrorDetail);
        return ResultResponse(result);
    }

    /// <summary>§4.1: a rate-limit denial answers 429 WITH <c>Retry-After</c> (seconds ride the error detail).</summary>
    private void SetRetryAfter(string? errorCode, string? errorDetail)
    {
        if (errorCode == "RATE_LIMITED" && errorDetail is not null)
            Response.Headers.RetryAfter = errorDetail;
    }
}
