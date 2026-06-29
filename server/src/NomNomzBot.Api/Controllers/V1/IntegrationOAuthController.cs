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
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The generic, descriptor-driven OAuth connect surface for non-Twitch providers (integrations-oauth §5):
/// Spotify + YouTube today, any future ordinary OAuth2 provider by adding a descriptor. Every endpoint delegates
/// to <see cref="IIntegrationOAuthService"/>, which performs the PKCE authorization-code dance and hands tokens
/// to the crypto vault — this controller stores nothing and knows no provider specifics. Discord's bespoke
/// guild/bot connect is owned by <c>discord.md</c> (see <see cref="DiscordOAuthController"/>), not this generic
/// flow.
/// <para>
/// Gate 1 is <c>[Authorize]</c> + tenant resolution from the route <c>channelId</c>. Gate 2 is the per-route
/// <c>[RequireAction]</c> floor (<c>integration:read</c> / <c>integration:write</c>, integrations-oauth §5),
/// enforced by <c>IActionAuthorizationService</c>; self-host collapses to "owner = full". The callback is
/// anonymous (a provider redirect cannot carry the JWT) and is secured instead by the service's signed,
/// single-use <c>state</c> nonce.
/// </para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize]
[Tags("Integration OAuth")]
public class IntegrationOAuthController : BaseController
{
    private readonly IIntegrationOAuthService _oauth;
    private readonly IConfiguration _config;

    public IntegrationOAuthController(IIntegrationOAuthService oauth, IConfiguration config)
    {
        _oauth = oauth;
        _config = config;
    }

    /// <summary>The integrations screen read model: per provider — connected?, account, granted scope-sets, capabilities.</summary>
    [HttpGet("channels/{channelId}/integrations/status")]
    [RequireAction("integration:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<IntegrationStatusDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetStatus(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<IReadOnlyList<IntegrationStatusDto>> result = await _oauth.GetStatusAsync(
            broadcasterId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Starts a provider connect: returns the authorize URL the client opens (the service builds the PKCE
    /// challenge + signed single-use state). A connect carries a progressive scope-set key (e.g.
    /// <c>spotify.playback</c>); re-connecting with a wider set is an incremental re-auth.
    /// </summary>
    [HttpPost("channels/{channelId}/integrations/{provider}/connect")]
    [RequireAction("integration:write")]
    [ProducesResponseType<StatusResponseDto<OAuthStartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Connect(
        string channelId,
        string provider,
        [FromBody] ConnectIntegrationRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(User.GetUserId(), out Guid actingUserId))
            return UnauthenticatedResponse();

        Result<OAuthStartDto> result = await _oauth.StartConnectAsync(
            broadcasterId,
            provider,
            request.ScopeSetKey,
            request.ReturnUrl,
            actingUserId,
            Request.ResolvePublicOrigin(_config),
            ct
        );

        // A start failure is always a client error (unknown provider / scope-set), not a server fault.
        return result.IsSuccess
            ? Ok(new StatusResponseDto<OAuthStartDto> { Data = result.Value })
            : BadRequestResponse(result.ErrorMessage);
    }

    /// <summary>
    /// The provider redirect target. Anonymous — secured by the signed single-use <c>state</c>, not the JWT.
    /// Validates+consumes state, exchanges the code (with the PKCE verifier) for tokens, vaults them, then
    /// redirects the browser back to the connect's return URL. A failure redirects to the integrations screen
    /// with an error marker rather than surfacing a raw API error to the browser.
    /// </summary>
    [HttpGet("integrations/{provider}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken ct
    )
    {
        Result<OAuthCallbackResultDto> result = await _oauth.HandleCallbackAsync(
            provider,
            new OAuthCallbackParams(code, state, error, errorDescription),
            ct
        );

        // Always bounce through /oauth-relay so popup windows can postMessage the parent and close
        // without loading the full Wasm app. The relay page falls back to navigating the original
        // target URL when there is no window.opener (full-page redirect fallback).
        string relay = $"{Request.Scheme}://{Request.Host}/oauth-relay";

        if (result.IsSuccess)
        {
            string encoded = Uri.EscapeDataString(result.Value.RedirectTarget);
            return Redirect($"{relay}?return={encoded}");
        }

        string reason = Uri.EscapeDataString(result.ErrorCode ?? "connect_failed");
        return Redirect($"{relay}?provider={Uri.EscapeDataString(provider)}&error={reason}");
    }

    /// <summary>Severs a provider connection: revokes the token where supported, then soft-deletes + crypto-shreds the vault entry. Idempotent.</summary>
    [HttpPost("channels/{channelId}/integrations/{provider}/disconnect")]
    [RequireAction("integration:write")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Disconnect(
        string channelId,
        string provider,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!Guid.TryParse(User.GetUserId(), out Guid actingUserId))
            return UnauthenticatedResponse();

        Result result = await _oauth.DisconnectAsync(broadcasterId, provider, actingUserId, ct);
        return ResultResponse(result);
    }
}
