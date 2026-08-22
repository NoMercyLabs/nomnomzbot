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
using NomNomzBot.Api.Extensions;
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
public class AutomationDataController(
    IAutomationCommandService commands,
    IAutomationPairingService pairing,
    IAutomationApiTokenService tokens,
    IConfiguration configuration
) : BaseController
{
    /// <summary>
    /// Redeem a device pairing code (stream-deck.md §4). The DEVICE has no credential yet — the code
    /// IS the credential — so this is the one anonymous data-plane action; it is single-use and
    /// brute-force guarded per caller AND globally, and a successful redeem returns the one-time
    /// automation token secret plus the backend URL the device should connect back to.
    /// </summary>
    [HttpPost("pair")]
    [AllowAnonymous]
    [ProducesResponseType<StatusResponseDto<PairingRedemptionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RedeemPairing(
        [FromBody] RedeemPairingCodeRequest request,
        CancellationToken ct
    )
    {
        string clientKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        Result<PairingRedemptionDto> result = await pairing.RedeemCodeAsync(
            request.Code,
            request.Device,
            clientKey,
            Request.ResolvePublicOrigin(configuration),
            ct
        );
        SetRetryAfter(result.ErrorCode, result.ErrorDetail);
        return ResultResponse(result);
    }

    /// <summary>
    /// Device-initiated pairing, step 1 (stream-deck.md D9): the DEVICE calls this itself, no dashboard
    /// interaction required — mirrors the Twitch device-code login this project already uses for bot
    /// auth. Returns an opaque device code to poll plus a short user code + verification URL an
    /// operator opens in any logged-in browser to approve.
    /// </summary>
    [HttpPost("pair/device/init")]
    [AllowAnonymous]
    [ProducesResponseType<StatusResponseDto<DeviceInitDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitDevicePairing(
        [FromBody] DeviceInitRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await pairing.InitDeviceAsync(
                request.Device,
                Request.ResolvePublicOrigin(configuration),
                request.Scopes,
                ct
            )
        );

    /// <summary>
    /// Device-initiated pairing, step 3: the device polls by its own device code until an operator
    /// approves it (step 2, <see cref="AutomationPairingController.ApproveDevice"/>). <c>deviceCode</c>
    /// is 256 bits of entropy — no brute-force guard needed the way the short human user code has one.
    /// </summary>
    [HttpPost("pair/device/poll")]
    [AllowAnonymous]
    [ProducesResponseType<StatusResponseDto<DevicePollDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PollDevicePairing(
        [FromBody] DevicePollRequest request,
        CancellationToken ct
    ) => ResultResponse(await pairing.PollDeviceAsync(request.DeviceCode, ct));

    /// <summary>Broadcaster + instance summary (scope <c>read</c>).</summary>
    [HttpGet("info")]
    [ProducesResponseType<StatusResponseDto<AutomationInfo>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInfo(CancellationToken ct)
    {
        if (Principal is not { } principal)
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
        if (Principal is not { } principal)
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
        if (Principal is not { } principal)
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
        if (Principal is not { } principal)
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
        if (Principal is not { } principal)
            return UnauthenticatedResponse();
        Result result = await commands.SendChatAsync(principal, request, ct);
        SetRetryAfter(result.ErrorCode, result.ErrorDetail);
        return ResultResponse(result);
    }

    /// <summary>
    /// Self-refresh the presented token's secret (stream-deck.md D8) — no scope requirement, the
    /// presented token IS the credential being refreshed. Called proactively by a paired device (a
    /// Stream Deck plugin) before its 30-day <c>ExpiresAt</c> lapses.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<StatusResponseDto<IssuedAutomationTokenDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (Principal is not { } principal)
            return UnauthenticatedResponse();
        return ResultResponse(
            await tokens.RefreshSelfAsync(principal.BroadcasterId, principal.TokenId, ct)
        );
    }

    /// <summary>Current playback state (scope <c>read</c>) — music-automation-controls.md §4.</summary>
    [HttpGet("music/now-playing")]
    [ProducesResponseType<StatusResponseDto<AutomationNowPlayingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNowPlaying(CancellationToken ct)
    {
        if (Principal is not { } principal)
            return UnauthenticatedResponse();
        return WithRetryAfter(await commands.GetNowPlayingAsync(principal, ct));
    }

    /// <summary>The broadcaster's playback devices on the active music provider (scope <c>read</c>).</summary>
    [HttpGet("music/devices")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<AutomationDeviceDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetMusicDevices(CancellationToken ct)
    {
        if (Principal is not { } principal)
            return UnauthenticatedResponse();
        return WithRetryAfter(await commands.GetDevicesAsync(principal, ct));
    }

    /// <summary>The broadcaster's playlists on the active music provider (scope <c>read</c>), paged.</summary>
    [HttpGet("music/playlists")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<AutomationPlaylistDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetMusicPlaylists(
        [FromQuery] int limit,
        [FromQuery] int offset,
        CancellationToken ct
    )
    {
        if (Principal is not { } principal)
            return UnauthenticatedResponse();
        return WithRetryAfter(
            await commands.GetPlaylistsAsync(
                principal,
                limit is > 0 and <= 50 ? limit : 20,
                Math.Max(0, offset),
                ct
            )
        );
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
