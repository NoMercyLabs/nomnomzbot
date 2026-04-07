// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) NoMercy Entertainment. All rights reserved.

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NoMercyBot.Api.Models;
using NoMercyBot.Application.Common.Models;
using NoMercyBot.Application.DTOs.Auth;
using NoMercyBot.Application.Features.Auth.Queries.GetCurrentUser;
using NoMercyBot.Application.Services;

namespace NoMercyBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Tags("Auth")]
public class AuthController : BaseController
{
    private readonly GetCurrentUserQueryHandler _getCurrentUser;
    private readonly IAuthService _authService;
    private readonly IConfiguration _config;

    public AuthController(
        GetCurrentUserQueryHandler getCurrentUser,
        IAuthService authService,
        IConfiguration config)
    {
        _getCurrentUser = getCurrentUser;
        _authService = authService;
        _config = config;
    }

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

    /// <summary>Get the currently authenticated user.</summary>
    [HttpGet("me")]
    [Authorize]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<CurrentUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        Result<CurrentUserDto> result = await _getCurrentUser.HandleAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Start the Twitch OAuth flow. Redirects the browser to Twitch's authorization page.
    /// Pass <c>redirect_uri</c> for mobile deep-link callbacks (e.g. <c>nomercybot://callback</c>).
    /// </summary>
    [HttpGet("twitch")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartTwitchOAuth([FromQuery] string? redirect_uri, CancellationToken ct)
    {
        // Encode the flow + optional mobile redirect URI into the state parameter so
        // the single callback endpoint can route the result correctly.
        var payload = JsonSerializer.Serialize(new { flow = "user", redirect_uri });
        string state = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

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
        CancellationToken ct)
    {
        string? mobileRedirectUri = null;
        string? channelId = null;
        string flow = "user";
        string callbackUri = $"{GetPublicBaseUrl()}/api/v1/auth/twitch/callback";

        if (!string.IsNullOrWhiteSpace(state))
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(state);
                using var doc = JsonDocument.Parse(decoded);

                if (doc.RootElement.TryGetProperty("flow", out var flowElement))
                    flow = flowElement.GetString() ?? "user";

                if (doc.RootElement.TryGetProperty("redirect_uri", out var uriElement))
                    mobileRedirectUri = uriElement.GetString();

                if (doc.RootElement.TryGetProperty("channel_id", out var channelElement))
                    channelId = channelElement.GetString();
            }
            catch
            {
                // State isn't our encoded payload — default to the normal user flow.
            }
        }

        if (flow == "bot")
        {
            Result<BotStatusDto> botResult = await _authService.HandleTwitchBotCallbackAsync(
                new OAuthCallbackDto { Code = code, RedirectUri = callbackUri },
                ct);

            if (botResult.IsFailure)
            {
                if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
                    return Redirect($"{mobileRedirectUri}?error=bot_auth_failed");

                return ResultResponse(botResult);
            }

            if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
                return Redirect($"{mobileRedirectUri}?bot_connected=true");

            string botName = botResult.Value.DisplayName ?? botResult.Value.Login ?? "Bot";
            string html = "<!DOCTYPE html><html><head><title>Bot Connected</title>"
                + "<style>"
                + "body{background:#141125;color:#f4f5fa;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}"
                + ".card{background:#1A1530;border:1px solid #1e1a35;border-radius:16px;padding:48px;text-align:center;max-width:420px}"
                + ".check{width:64px;height:64px;border-radius:50%;background:rgba(74,222,128,0.15);display:flex;align-items:center;justify-content:center;margin:0 auto 24px;font-size:32px;color:#4ade80}"
                + "h1{font-size:24px;margin:0 0 8px}p{color:#8889a0;font-size:14px;margin:0}.name{color:#a78bfa;font-weight:600}"
                + "</style></head><body>"
                + "<div class='card'>"
                + "<div class='check'>&#10003;</div>"
                + "<h1>Bot Connected</h1>"
                + "<p><span class='name'>" + botName + "</span> has been authorized successfully.</p>"
                + "<p style='margin-top:16px'>You can close this tab and return to the setup wizard.</p>"
                + "</div></body></html>";
            return Content(html, "text/html");
        }

        if (flow == "channel_bot")
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return BadRequest("Missing channel_id in state.");

            Result<BotStatusDto> channelBotResult = await _authService.HandleTwitchChannelBotCallbackAsync(
                channelId,
                new OAuthCallbackDto { Code = code, RedirectUri = callbackUri },
                ct);

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
            new OAuthCallbackDto { Code = code, State = state, RedirectUri = callbackUri },
            ct);

        if (result.IsFailure)
        {
            if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
            {
                return Redirect(
                    $"{mobileRedirectUri}?error=auth_failed&error_description={Uri.EscapeDataString(result.ErrorMessage ?? "Authentication failed")}");
            }

            return ResultResponse(result);
        }

        AuthResultDto auth = result.Value;
        int expiresIn = (int)(auth.ExpiresAt - DateTime.UtcNow).TotalSeconds;

        if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
        {
            var qs = new StringBuilder(mobileRedirectUri);
            qs.Append(mobileRedirectUri.Contains('?') ? '&' : '?');
            qs.Append("access_token=").Append(Uri.EscapeDataString(auth.AccessToken));
            qs.Append("&refresh_token=").Append(Uri.EscapeDataString(auth.RefreshToken));
            qs.Append("&expires_in=").Append(expiresIn);

            return Redirect(qs.ToString());
        }

        return Ok(new StatusResponseDto<object>
        {
            Data = new
            {
                accessToken = auth.AccessToken,
                refreshToken = auth.RefreshToken,
                expiresIn,
                user = auth.User,
            },
        });
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
        CancellationToken ct)
    {
        Result<AuthResultDto> result = await _authService.HandleTwitchCallbackAsync(body, ct);

        if (result.IsFailure)
            return ResultResponse(result);

        AuthResultDto auth = result.Value;
        int expiresIn = (int)(auth.ExpiresAt - DateTime.UtcNow).TotalSeconds;

        return Ok(new StatusResponseDto<object>
        {
            Data = new
            {
                accessToken = auth.AccessToken,
                refreshToken = auth.RefreshToken,
                expiresIn,
                user = auth.User,
            },
        });
    }

    /// <summary>Refresh an expired access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        CancellationToken ct)
    {
        Result<AuthResultDto> result = await _authService.RefreshTokenAsync(request.RefreshToken, ct);

        if (result.IsFailure)
            return ResultResponse(result);

        AuthResultDto auth = result.Value;
        int expiresIn = (int)(auth.ExpiresAt - DateTime.UtcNow).TotalSeconds;

        return Ok(new StatusResponseDto<object>
        {
            Data = new
            {
                accessToken = auth.AccessToken,
                refreshToken = auth.RefreshToken,
                expiresIn,
                user = auth.User,
            },
        });
    }

    /// <summary>Log out the current user, revoking their Twitch tokens.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return UnauthenticatedResponse();

        Result result = await _authService.LogoutAsync(userId, ct);
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
    public async Task<IActionResult> StartBotOAuth([FromQuery] string? redirect_uri, CancellationToken ct)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { flow = "bot", redirect_uri });
        string state = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));

        string authUrl = await _authService.GetTwitchBotOAuthUrl(state, GetPublicBaseUrl(), ct);
        return Redirect(authUrl);
    }

    /// <summary>
    /// Handle the Twitch callback for the bot account.
    /// Stores the token as "twitch_bot" and redirects to the dashboard integrations page.
    /// </summary>
    [HttpGet("twitch/bot/callback")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> HandleBotCallback(
        [FromQuery] string code,
        [FromQuery] string? state,
        CancellationToken ct)
    {
        string? mobileRedirectUri = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            try
            {
                byte[] decoded = Convert.FromBase64String(state);
                using var doc = System.Text.Json.JsonDocument.Parse(decoded);
                if (doc.RootElement.TryGetProperty("redirect_uri", out var uriElement))
                    mobileRedirectUri = uriElement.GetString();
            }
            catch { }
        }

        string callbackUri = $"{GetPublicBaseUrl()}/api/v1/auth/twitch/bot/callback";
        Result<BotStatusDto> result = await _authService.HandleTwitchBotCallbackAsync(
            new OAuthCallbackDto { Code = code, RedirectUri = callbackUri }, ct);

        if (result.IsFailure)
        {
            if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
                return Redirect($"{mobileRedirectUri}?error=bot_auth_failed");

            return ResultResponse(result);
        }

        // Redirect back to the integrations page on success
        if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
            return Redirect($"{mobileRedirectUri}?bot_connected=true");

        // Web: show a success page — the user likely authorized in a different browser,
        // so redirecting to the app would show a login page (confusing).
        string botName = result.Value.DisplayName ?? result.Value.Login ?? "Bot";
        string html = "<!DOCTYPE html><html><head><title>Bot Connected</title>"
            + "<style>"
            + "body{background:#141125;color:#f4f5fa;font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}"
            + ".card{background:#1A1530;border:1px solid #1e1a35;border-radius:16px;padding:48px;text-align:center;max-width:420px}"
            + ".check{width:64px;height:64px;border-radius:50%;background:rgba(74,222,128,0.15);display:flex;align-items:center;justify-content:center;margin:0 auto 24px;font-size:32px;color:#4ade80}"
            + "h1{font-size:24px;margin:0 0 8px}p{color:#8889a0;font-size:14px;margin:0}.name{color:#a78bfa;font-weight:600}"
            + "</style></head><body>"
            + "<div class='card'>"
            + "<div class='check'>&#10003;</div>"
            + "<h1>Bot Connected</h1>"
            + "<p><span class='name'>" + botName + "</span> has been authorized successfully.</p>"
            + "<p style='margin-top:16px'>You can close this tab and return to the setup wizard.</p>"
            + "</div></body></html>";
        return Content(html, "text/html");
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
