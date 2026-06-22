// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Claims;
using System.Text;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Tags("Auth")]
public class AuthController : BaseController
{
    private readonly IUserService _userService;
    private readonly IAuthService _authService;
    private readonly IConfiguration _config;
    private readonly TimeProvider _timeProvider;
    private readonly ITwitchOAuthStateService _oauthState;

    public AuthController(
        IUserService userService,
        IAuthService authService,
        IConfiguration config,
        TimeProvider timeProvider,
        ITwitchOAuthStateService oauthState
    )
    {
        _userService = userService;
        _authService = authService;
        _config = config;
        _timeProvider = timeProvider;
        _oauthState = oauthState;
    }

    // Mobile deep-link callbacks may only target the app's own custom scheme — never an arbitrary URL — so a
    // phishing link cannot redirect the post-auth response (and its tokens) to an attacker (§5). A blank value
    // is the normal web flow (JSON response, no redirect).
    private static bool IsAllowedMobileRedirect(string? redirectUri) =>
        string.IsNullOrWhiteSpace(redirectUri)
        || redirectUri.StartsWith("nomnomzbot://", StringComparison.OrdinalIgnoreCase);

    private string GetPublicBaseUrl()
    {
        string forwardedHost = Request.Headers["X-Forwarded-Host"].ToString();
        string forwardedProto = Request.Headers["X-Forwarded-Proto"].ToString();

        string? host = !string.IsNullOrWhiteSpace(forwardedHost)
            ? forwardedHost.Split(',')[0].Trim()
            : Request.Host.Value;

        string? scheme = !string.IsNullOrWhiteSpace(forwardedProto)
            ? forwardedProto.Split(',')[0].Trim()
            : Request.Scheme;

        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(scheme))
            return $"{scheme}://{host}";

        return (_config["App:BaseUrl"] ?? "http://localhost:5080").TrimEnd('/');
    }

    /// <summary>
    /// The request fingerprint for a new login session: client class (web by default; clients may pass
    /// <c>X-Client-Type</c>), source IP, and user-agent.
    /// </summary>
    private AuthContextDto BuildAuthContext()
    {
        string clientType = Request.Headers["X-Client-Type"].ToString();
        if (string.IsNullOrWhiteSpace(clientType))
            clientType = "web";

        string? ip = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
        string? userAgent = Request.Headers.UserAgent.ToString();
        return new AuthContextDto(
            clientType,
            ip,
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent
        );
    }

    /// <summary>Get the currently authenticated user.</summary>
    [HttpGet("me")]
    [Authorize]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<CurrentUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        Result<CurrentUserDto> result = await _userService.GetCurrentUserAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Start the Twitch OAuth flow. Redirects the browser to Twitch's authorization page.
    /// Pass <c>redirect_uri</c> for mobile deep-link callbacks (e.g. <c>nomnomzbot://callback</c>).
    /// </summary>
    [HttpGet("twitch")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartTwitchOAuth(
        [FromQuery] string? redirect_uri,
        CancellationToken ct
    )
    {
        if (!IsAllowedMobileRedirect(redirect_uri))
            return BadRequest("Disallowed redirect_uri.");

        // Issue a single-use, server-side CSRF state nonce; only the opaque nonce travels through Twitch,
        // and the flow + optional mobile redirect are held server-side so the callback can route safely.
        string state = await _oauthState.IssueAsync(
            new TwitchOAuthFlowState("user", redirect_uri),
            ct
        );

        string authUrl = await _authService.GetTwitchOAuthUrl(state, GetPublicBaseUrl(), ct);
        return Redirect(authUrl);
    }

    /// <summary>
    /// Handle the OAuth callback from Twitch. Exchanges the authorization code for
    /// platform tokens. If a mobile <c>redirect_uri</c> was passed in the original
    /// request, the browser is redirected to the app's deep link with tokens in the
    /// query string. Otherwise returns a JSON response (for web clients).
    /// </summary>
    [HttpGet("twitch/callback")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> HandleTwitchCallback(
        [FromQuery] string code,
        [FromQuery] string? state,
        CancellationToken ct
    )
    {
        string callbackUri = $"{GetPublicBaseUrl()}/api/v1/auth/twitch/callback";

        // Consume the single-use CSRF state nonce. A missing, expired, or forged nonce is rejected here — the
        // flow, mobile redirect, and channel id come only from the server-side payload, never the query string.
        TwitchOAuthFlowState? flowState = await _oauthState.ConsumeAsync(state, ct);
        if (flowState is null)
            return BadRequest("Invalid or expired OAuth state.");

        string flow = flowState.Flow;
        string? mobileRedirectUri = flowState.RedirectUri;
        string? channelId = flowState.ChannelId;

        if (flow == "bot")
        {
            Result<BotStatusDto> botResult = await _authService.HandleTwitchBotCallbackAsync(
                new OAuthCallbackDto { Code = code, RedirectUri = callbackUri },
                ct
            );

            if (botResult.IsFailure)
            {
                if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
                    return Redirect($"{mobileRedirectUri}?error=bot_auth_failed");

                return ResultResponse(botResult);
            }

            if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
                return Redirect($"{mobileRedirectUri}?bot_connected=true");

            string botName = botResult.Value.DisplayName ?? botResult.Value.Login ?? "Bot";
            string html =
                "<!DOCTYPE html><html><head><title>Bot Connected</title>"
                + "<style>"
                + "body{background:#141125;color:#f4f5fa;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}"
                + ".card{background:#1A1530;border:1px solid #1e1a35;border-radius:16px;padding:48px;text-align:center;max-width:420px}"
                + ".check{width:64px;height:64px;border-radius:50%;background:rgba(74,222,128,0.15);display:flex;align-items:center;justify-content:center;margin:0 auto 24px;font-size:32px;color:#4ade80}"
                + "h1{font-size:24px;margin:0 0 8px}p{color:#8889a0;font-size:14px;margin:0}.name{color:#a78bfa;font-weight:600}"
                + "</style></head><body>"
                + "<div class='card'>"
                + "<div class='check'>&#10003;</div>"
                + "<h1>Bot Connected</h1>"
                + "<p><span class='name'>"
                + botName
                + "</span> has been authorized successfully.</p>"
                + "<p style='margin-top:16px'>You can close this tab and return to the setup wizard.</p>"
                + "</div></body></html>";
            return Content(html, "text/html");
        }

        if (flow == "channel_bot")
        {
            if (
                string.IsNullOrWhiteSpace(channelId)
                || !Guid.TryParse(channelId, out Guid channelTenantId)
            )
                return BadRequest("Missing or invalid channel_id in state.");

            Result<BotStatusDto> channelBotResult =
                await _authService.HandleTwitchChannelBotCallbackAsync(
                    channelTenantId,
                    new OAuthCallbackDto { Code = code, RedirectUri = callbackUri },
                    ct
                );

            if (channelBotResult.IsFailure)
            {
                if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
                    return Redirect($"{mobileRedirectUri}?error=bot_auth_failed");

                return ResultResponse(channelBotResult);
            }

            if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
                return Redirect($"{mobileRedirectUri}?custom_bot_connected=true");

            string frontendUrl = _config["App:FrontendUrl"] ?? "https://bot-dev.nomercy.tv";
            return Redirect($"{frontendUrl}/(dashboard)/integrations?custom_bot_connected=true");
        }

        Result<AuthResultDto> result = await _authService.HandleTwitchCallbackAsync(
            new OAuthCallbackDto
            {
                Code = code,
                State = state,
                RedirectUri = callbackUri,
            },
            BuildAuthContext(),
            ct
        );

        if (result.IsFailure)
        {
            if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
            {
                return Redirect(
                    $"{mobileRedirectUri}?error=auth_failed&error_description={Uri.EscapeDataString(result.ErrorMessage ?? "Authentication failed")}"
                );
            }

            return ResultResponse(result);
        }

        AuthResultDto auth = result.Value;
        int expiresIn = (int)(auth.ExpiresAt - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds;

        if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
        {
            StringBuilder qs = new StringBuilder(mobileRedirectUri);
            qs.Append(mobileRedirectUri.Contains('?') ? '&' : '?');
            qs.Append("access_token=").Append(Uri.EscapeDataString(auth.AccessToken));
            qs.Append("&refresh_token=").Append(Uri.EscapeDataString(auth.RefreshToken));
            qs.Append("&expires_in=").Append(expiresIn);

            return Redirect(qs.ToString());
        }

        return Ok(
            new StatusResponseDto<object>
            {
                Data = new
                {
                    accessToken = auth.AccessToken,
                    refreshToken = auth.RefreshToken,
                    expiresIn,
                    user = auth.User,
                },
            }
        );
    }

    /// <summary>
    /// Exchange an OAuth authorization code for platform tokens (mobile / SPA flow).
    /// The client handles the Twitch redirect directly and sends the code + redirect_uri
    /// to this endpoint for server-side token exchange.
    /// </summary>
    [HttpPost("twitch/callback")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ExchangeCode(
        [FromBody] OAuthCallbackDto body,
        CancellationToken ct
    )
    {
        Result<AuthResultDto> result = await _authService.HandleTwitchCallbackAsync(
            body,
            BuildAuthContext(),
            ct
        );

        if (result.IsFailure)
            return ResultResponse(result);

        AuthResultDto auth = result.Value;
        int expiresIn = (int)(auth.ExpiresAt - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds;

        return Ok(
            new StatusResponseDto<object>
            {
                Data = new
                {
                    accessToken = auth.AccessToken,
                    refreshToken = auth.RefreshToken,
                    expiresIn,
                    user = auth.User,
                },
            }
        );
    }

    /// <summary>Refresh an expired access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct
    )
    {
        Result<AuthResultDto> result = await _authService.RefreshTokenAsync(
            request.RefreshToken,
            BuildAuthContext(),
            ct
        );

        if (result.IsFailure)
            return ResultResponse(result);

        AuthResultDto auth = result.Value;
        int expiresIn = (int)(auth.ExpiresAt - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds;

        return Ok(
            new StatusResponseDto<object>
            {
                Data = new
                {
                    accessToken = auth.AccessToken,
                    refreshToken = auth.RefreshToken,
                    expiresIn,
                    user = auth.User,
                },
            }
        );
    }

    /// <summary>Log out the current session, revoking its refresh tokens.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        string? sessionId = User.FindFirstValue("sid");
        if (
            !Guid.TryParse(userId, out Guid userGuid)
            || !Guid.TryParse(sessionId, out Guid sessionGuid)
        )
            return UnauthenticatedResponse();

        Result result = await _authService.LogoutAsync(userGuid, sessionGuid, ct);
        return ResultResponse(result);
    }

    /// <summary>Log out of all the current user's sessions, revoking every refresh token they hold.</summary>
    [HttpPost("logout/all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out Guid userGuid))
            return UnauthenticatedResponse();

        Result<int> result = await _authService.LogoutAllAsync(userGuid, ct);
        return ResultResponse(result);
    }

    // ── Bot account OAuth ─────────────────────────────────────────────────────

    /// <summary>
    /// Start the Twitch OAuth flow for the bot account.
    /// The authenticated user authorizes a SECOND Twitch account (the bot) with chat scopes.
    /// The resulting token is stored globally (no per-channel binding) as "twitch_bot".
    /// </summary>
    [HttpGet("twitch/bot")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartBotOAuth(
        [FromQuery] string? redirect_uri,
        CancellationToken ct
    )
    {
        if (!IsAllowedMobileRedirect(redirect_uri))
            return BadRequest("Disallowed redirect_uri.");

        string state = await _oauthState.IssueAsync(
            new TwitchOAuthFlowState("bot", redirect_uri),
            ct
        );

        string authUrl = await _authService.GetTwitchBotOAuthUrl(state, GetPublicBaseUrl(), ct);
        return Redirect(authUrl);
    }

    /// <summary>Get the current bot account connection status.</summary>
    [HttpGet("twitch/bot/status")]
    [Authorize]
    public async Task<IActionResult> GetBotStatus(CancellationToken ct)
    {
        Result<BotStatusDto> result = await _authService.GetBotStatusAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>Disconnect the bot account, revoking its Twitch token.</summary>
    [HttpDelete("twitch/bot")]
    [Authorize]
    public async Task<IActionResult> DisconnectBot(CancellationToken ct)
    {
        Result result = await _authService.DisconnectBotAsync(ct);
        return ResultResponse(result);
    }
}
