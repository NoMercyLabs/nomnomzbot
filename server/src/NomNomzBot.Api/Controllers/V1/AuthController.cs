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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Twitch sign-in (redirect + device-code flows), JWT issue/refresh, and session logout.</summary>
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
    private readonly ILoginProviderRegistry _loginProviders;

    public AuthController(
        IUserService userService,
        IAuthService authService,
        IConfiguration config,
        TimeProvider timeProvider,
        ITwitchOAuthStateService oauthState,
        ILoginProviderRegistry loginProviders
    )
    {
        _userService = userService;
        _authService = authService;
        _config = config;
        _timeProvider = timeProvider;
        _oauthState = oauthState;
        _loginProviders = loginProviders;
    }

    private string GetPublicBaseUrl() => Request.ResolvePublicOrigin(_config);

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
    /// Pass <c>redirect_uri</c> for mobile / desktop-loopback deep-link callbacks (e.g.
    /// <c>nomnomzbot://callback</c>). Pass <c>client=web</c> for the served-web dashboard: a full-page redirect
    /// can't receive a JSON token body, so the callback returns the tokens in the URL fragment + an HttpOnly
    /// cookie instead.
    /// </summary>
    [HttpGet("twitch")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartTwitchOAuth(
        [FromQuery] string? redirect_uri,
        [FromQuery] string? client,
        CancellationToken ct
    )
    {
        if (!ClientRedirectPolicy.IsAllowed(redirect_uri))
            return BadRequest("Disallowed redirect_uri.");

        // Issue a single-use, server-side CSRF state nonce; only the opaque nonce travels through Twitch, and
        // the flow + optional mobile redirect + client class are held server-side so the callback can route
        // safely (the client class can't be tampered with in the query string on the way back).
        string state = await _oauthState.IssueAsync(
            new TwitchOAuthFlowState("user", redirect_uri, Client: client),
            ct
        );

        Result<string> authUrl = await _authService.GetTwitchOAuthUrl(
            state,
            GetPublicBaseUrl(),
            ct
        );
        if (authUrl.IsFailure)
            return ResultResponse(authUrl);
        return Redirect(authUrl.Value);
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
        string? client = flowState.Client;

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
                + "</div>"
                + "<script>"
                + "if(window.opener){try{window.opener.postMessage({type:'oauth_relay',bot_connected:'true'},window.location.origin);}catch(_){}setTimeout(function(){window.close();},800);}"
                + "</script>"
                + "</body></html>";
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

            // Route through /oauth-relay so popup windows can postMessage the parent and close.
            return Redirect(
                $"{Request.Scheme}://{Request.Host}/oauth-relay?custom_bot_connected=true"
            );
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

        // Served-web dashboard: the page navigated the whole window to Twitch, so it can't read a JSON body on
        // the way back. Hand the access token to the SPA in the URL fragment (never sent to the server, so it
        // stays out of logs/Referer) and keep the long-lived refresh token in an HttpOnly + Secure + Lax cookie
        // the SPA's JS can't read — defeating token exfiltration via XSS.
        if (string.Equals(client, "web", StringComparison.OrdinalIgnoreCase))
        {
            SetRefreshTokenCookie(auth.RefreshToken);

            string fragment =
                $"#access_token={Uri.EscapeDataString(auth.AccessToken)}&expires_in={expiresIn}";
            return Redirect($"{GetPublicBaseUrl()}/{fragment}");
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
    /// Stores the refresh token in a hardened cookie for the served-web flow: <c>HttpOnly</c> (JS can't read it,
    /// so XSS can't exfiltrate it), <c>Secure</c> (HTTPS only), <c>SameSite=Lax</c> (sent on the top-level
    /// OAuth-return navigation but not on cross-site sub-requests), scoped to the refresh endpoint path.
    /// </summary>
    private void SetRefreshTokenCookie(string refreshToken) =>
        Response.Cookies.Append(
            "nnz_refresh_token",
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                // Secure when actually served over HTTPS (production / the dev tunnel); relaxed for a plain
                // http://localhost self-host so the cookie still works there. localhost is a trusted origin.
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/api/v1/auth",
                Expires = _timeProvider.GetUtcNow().AddDays(30),
            }
        );

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

    /// <summary>
    /// Begin the no-secret streamer login (Device Code Flow). Returns a short user code + the
    /// twitch.tv/activate URL to show the operator, plus the device code the client polls with. No Twitch app
    /// registration and no client secret — NomNomzBot's shipped public client id drives it.
    /// </summary>
    [HttpPost("twitch/device")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<DeviceCodeStartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartTwitchDeviceLogin(CancellationToken ct)
    {
        Result<DeviceCodeStartDto> result = await _authService.StartTwitchDeviceLoginAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Poll a streamer device login once. Until the operator approves, the status is <c>pending</c> /
    /// <c>slow_down</c>; on <c>authorized</c> the session is opened and the response carries the platform
    /// tokens + user, which the client establishes exactly as it would after the redirect callback.
    /// </summary>
    [HttpPost("twitch/device/poll")]
    [AllowAnonymous]
    [EnableRateLimiting("device-poll")]
    [ProducesResponseType<StatusResponseDto<DeviceLoginPollDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PollTwitchDeviceLogin(
        [FromBody] DevicePollRequest body,
        [FromQuery] string? client,
        CancellationToken ct
    )
    {
        Result<DeviceLoginPollDto> result = await _authService.PollTwitchDeviceLoginAsync(
            body.DeviceCode,
            BuildAuthContext(),
            ct
        );

        if (result.IsFailure)
            return ResultResponse(result);

        // Served-web dashboard: on approval, keep the long-lived refresh token in an HttpOnly + Secure cookie
        // the SPA's JS can never read (XSS can't exfiltrate it) and strip it from the JSON body. The browser
        // attaches the cookie automatically on the same-origin refresh call. Native clients keep the
        // token-in-body custody (a file/keychain vault — no browser XSS surface).
        DeviceLoginPollDto poll = result.Value;
        if (
            string.Equals(client, "web", StringComparison.OrdinalIgnoreCase)
            && poll.Status == DeviceLoginStatus.Authorized
            && poll.Auth is not null
        )
        {
            SetRefreshTokenCookie(poll.Auth.RefreshToken);
            return Ok(
                new StatusResponseDto<DeviceLoginPollDto>
                {
                    Data = poll with { Auth = poll.Auth with { RefreshToken = string.Empty } },
                }
            );
        }

        return ResultResponse(result);
    }

    /// <summary>
    /// The login providers this deployment offers (platform-identity §5). Public — the login screen reads it
    /// before any auth. Each carries the handshake flows the client should run and whether it is currently
    /// enabled (a registered descriptor whose feature flag resolves true); disabled ones are returned so the
    /// client can show "coming soon" rather than a dead button. Twitch is always enabled.
    /// </summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<LoginProviderDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetLoginProviders(CancellationToken ct)
    {
        IReadOnlyList<LoginProviderDescriptor> enabled = await _loginProviders.EnabledAsync(ct);
        HashSet<string> enabledKeys = enabled.Select(d => d.Key).ToHashSet();

        List<LoginProviderDto> providers = _loginProviders
            .All.Select(d => new LoginProviderDto(
                d.Key,
                d.DisplayName,
                FlowTokens(d.SupportedFlows),
                enabledKeys.Contains(d.Key)
            ))
            .ToList();

        return Ok(new StatusResponseDto<IReadOnlyList<LoginProviderDto>> { Data = providers });
    }

    /// <summary>
    /// Begin the no-secret device login for a provider (platform-identity §5). The literal <c>twitch/device</c>
    /// route is the <c>provider=twitch</c> case (literal segments win in routing), so existing clients are
    /// unaffected; this handles every other enabled provider. 404 <c>UNKNOWN_PROVIDER</c> / 403
    /// <c>PROVIDER_DISABLED</c> gate it.
    /// </summary>
    [HttpPost("{provider}/device")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<DeviceCodeStartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartDeviceLogin(string provider, CancellationToken ct)
    {
        IActionResult? gate = await ValidateEnabledProviderAsync(provider, ct);
        if (gate is not null)
            return gate;

        if (string.Equals(provider, AuthEnums.Platform.Twitch, StringComparison.OrdinalIgnoreCase))
            return ResultResponse(await _authService.StartTwitchDeviceLoginAsync(ct));

        return NotYetLoginable(provider);
    }

    /// <summary>
    /// Poll a provider device login once (platform-identity §5). Delegates the Twitch case to the established
    /// streamer-session flow (incl. the web HttpOnly-cookie custody); other enabled providers plug in with
    /// their login seams.
    /// </summary>
    [HttpPost("{provider}/device/poll")]
    [AllowAnonymous]
    [EnableRateLimiting("device-poll")]
    [ProducesResponseType<StatusResponseDto<DeviceLoginPollDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PollDeviceLogin(
        string provider,
        [FromBody] DevicePollRequest body,
        [FromQuery] string? client,
        CancellationToken ct
    )
    {
        IActionResult? gate = await ValidateEnabledProviderAsync(provider, ct);
        if (gate is not null)
            return gate;

        if (string.Equals(provider, AuthEnums.Platform.Twitch, StringComparison.OrdinalIgnoreCase))
            return await PollTwitchDeviceLogin(body, client, ct);

        return NotYetLoginable(provider);
    }

    /// <summary>Wire tokens for the flows a provider supports (<c>device_code</c>/<c>auth_code_pkce</c>/<c>auth_code</c>).</summary>
    private static IReadOnlyList<string> FlowTokens(LoginFlows flows)
    {
        List<string> tokens = [];
        if (flows.HasFlag(LoginFlows.DeviceCode))
            tokens.Add("device_code");
        if (flows.HasFlag(LoginFlows.AuthCodePkce))
            tokens.Add("auth_code_pkce");
        if (flows.HasFlag(LoginFlows.AuthCode))
            tokens.Add("auth_code");
        return tokens;
    }

    /// <summary>404 if the provider is unknown, 403 if it is registered but disabled; otherwise null (proceed).</summary>
    private async Task<IActionResult?> ValidateEnabledProviderAsync(
        string provider,
        CancellationToken ct
    )
    {
        if (_loginProviders.Get(provider).IsFailure)
            return NotFoundResponse($"Unknown login provider '{provider}'.");

        IReadOnlyList<LoginProviderDescriptor> enabled = await _loginProviders.EnabledAsync(ct);
        bool isEnabled = enabled.Any(d =>
            string.Equals(d.Key, provider, StringComparison.OrdinalIgnoreCase)
        );

        return isEnabled ? null : UnauthorizedResponse($"Login provider '{provider}' is disabled.");
    }

    private IActionResult NotYetLoginable(string provider) =>
        StatusCode(
            StatusCodes.Status501NotImplemented,
            new StatusResponseDto<object>
            {
                Status = "error",
                Message = $"Device login for '{provider}' is not yet implemented.",
            }
        );

    /// <summary>Refresh an expired access token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest? request,
        [FromQuery] string? client,
        CancellationToken ct
    )
    {
        // Web sends no body token — its refresh token rides an HttpOnly cookie the browser attaches
        // automatically; native sends the token it holds in its own vault. Prefer the body, fall back to the
        // cookie, and reject when neither is present.
        string? refreshToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
            refreshToken = Request.Cookies["nnz_refresh_token"];
        if (string.IsNullOrWhiteSpace(refreshToken))
            return UnauthenticatedResponse();

        Result<AuthResultDto> result = await _authService.RefreshTokenAsync(
            refreshToken,
            BuildAuthContext(),
            ct
        );

        if (result.IsFailure)
            return ResultResponse(result);

        AuthResultDto auth = result.Value;
        int expiresIn = (int)(auth.ExpiresAt - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds;

        // Web: rotate the HttpOnly cookie and DON'T hand the refresh token back in the JS-readable body.
        if (string.Equals(client, "web", StringComparison.OrdinalIgnoreCase))
        {
            SetRefreshTokenCookie(auth.RefreshToken);
            return Ok(
                new StatusResponseDto<object>
                {
                    Data = new
                    {
                        accessToken = auth.AccessToken,
                        expiresIn,
                        user = auth.User,
                    },
                }
            );
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
    /// Start the OAuth flow for the platform-shared bot account. This is a platform-level registration on top
    /// of the operator's account (not a login), so it is Plane-C gated (identity-auth §5,
    /// <c>platform · iam:manage</c> — the policy name is the IAM permission key verbatim, audited on SaaS).
    /// Returns the Twitch authorize URL for the client to open — the resulting token is stored globally (no
    /// per-channel binding). First-run setup uses the wizard's <c>system/setup/bot/oauth-url</c> instead
    /// (before an admin exists).
    /// </summary>
    [HttpGet("twitch/bot")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartBotOAuth(
        [FromQuery] string? redirect_uri,
        CancellationToken ct
    )
    {
        if (!ClientRedirectPolicy.IsAllowed(redirect_uri))
            return BadRequest("Disallowed redirect_uri.");

        string state = await _oauthState.IssueAsync(
            new TwitchOAuthFlowState("bot", redirect_uri),
            ct
        );

        Result<string> authUrl = await _authService.GetTwitchBotOAuthUrl(
            state,
            GetPublicBaseUrl(),
            ct
        );
        if (authUrl.IsFailure)
            return ResultResponse(authUrl);
        return Ok(
            new StatusResponseDto<OAuthStartDto> { Data = new OAuthStartDto(authUrl.Value, state) }
        );
    }

    /// <summary>Get the current platform-shared bot account connection status (platform-operator only).</summary>
    [HttpGet("twitch/bot/status")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    public async Task<IActionResult> GetBotStatus(CancellationToken ct)
    {
        Result<BotStatusDto> result = await _authService.GetBotStatusAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>Disconnect the platform-shared bot account, revoking its Twitch token (platform-operator only).</summary>
    [HttpDelete("twitch/bot")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    public async Task<IActionResult> DisconnectBot(CancellationToken ct)
    {
        Result result = await _authService.DisconnectBotAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>
    /// Begin the bot-account device login (no secret). The operator approves the bot's own Twitch account at
    /// twitch.tv/activate and the client polls until connected. Admin-gated — the streamer is already signed in
    /// when they add a bot account (Streamer.bot parity).
    /// </summary>
    [HttpPost("twitch/bot/device")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<StatusResponseDto<DeviceCodeStartDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartBotDeviceLogin(CancellationToken ct)
    {
        Result<DeviceCodeStartDto> result = await _authService.StartBotDeviceLoginAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>Poll a bot device login once; on <c>authorized</c> the shared bot account is connected + vaulted.</summary>
    [HttpPost("twitch/bot/device/poll")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [EnableRateLimiting("device-poll")]
    [ProducesResponseType<StatusResponseDto<DeviceBotPollDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PollBotDeviceLogin(
        [FromBody] DevicePollRequest body,
        CancellationToken ct
    )
    {
        Result<DeviceBotPollDto> result = await _authService.PollBotDeviceLoginAsync(
            body.DeviceCode,
            ct
        );
        return ResultResponse(result);
    }
}
