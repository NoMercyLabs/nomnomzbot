// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Deployment;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Twitch identity/login + bot OAuth (identity-auth §3.1). Builds the Twitch authorize URLs, exchanges the
/// code, upserts the <c>User</c> (and the owning <c>Channel</c> tenant root on first login) keyed by
/// <see cref="Guid"/> with the Twitch ids in attribute columns, vaults the Twitch tokens via
/// <see cref="IIntegrationTokenVault"/> (no flat <c>Service</c> row), opens a session via
/// <see cref="ISessionService"/>, and issues the platform JWT. The bot flows manage the shared/custom
/// <c>BotAccount</c> + per-channel <c>ChannelBotAuthorization</c>.
/// </summary>
public sealed class AuthService : IAuthService
{
    private const string PlatformBotProvider = AuthEnums.IntegrationProvider.Twitch + "_bot";

    private const string TwitchProvider = AuthEnums.IntegrationProvider.Twitch;

    private readonly IApplicationDbContext _db;
    private readonly ITwitchAuthService _twitchAuth;
    private readonly ITwitchDeviceCodeService _deviceCode;
    private readonly IIntegrationTokenVault _vault;
    private readonly ISessionService _sessions;
    private readonly IEventBus _eventBus;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;
    private readonly string _baseUrl;

    // Twitch user id of the account to promote to platform admin on login (§12 first-admin bootstrap).
    private readonly string? _initialAdminTwitchId;

    // Self-host makes the first onboarded account the platform admin (the owner IS the admin); SaaS does not.
    private readonly bool _isSelfHost;

    // SelfHostLite is single-streamer: a second login attaches to the existing channel instead of creating one.
    private readonly DeploymentMode _deploymentMode;

    private static readonly string[] RequiredScopes =
    [
        "user:read:email",
        "user:read:chat",
        // The streamer's own account is the bot's chat identity until a custom bot account is registered
        // (onboarding.md two-account model; deployment-profile.md "self-host always custom"), so the
        // streamer grant must carry the Helix chat-send scope — `HelixChatProvider` sends via
        // `POST /helix/chat/messages` (`user:write:chat`) on every profile (scaling-qos.md §6).
        "user:write:chat",
        // The chatbot badge: the broadcaster grants `channel:bot` to let the bot appear WITH THE BOT BADGE in
        // THEIR channel — the broadcaster-side half of the app-token send. The bot-side half (`user:bot`) rides
        // BotScopes. This is the proven recipe (the reference bot uses channel:bot on the broadcaster + user:bot
        // on the bot + an app-token send). `user:bot` deliberately does NOT ride this streamer grant: the
        // streamer's own account is not the chat sender on a dedicated-bot deployment, so requesting it here
        // only adds a confusing "appear as a bot" consent for no gain.
        "channel:bot",
        // The chat composer's emote picker shows the operator's own usable emotes across every channel they're
        // subscribed to (Get User Emotes, chat-client.md §3.2). It's an always-on part of the chat page, not a
        // feature toggle, so — like the rest of this full-set-at-login grant — its scope rides the base grant
        // rather than a per-feature progressive card. Without it Get User Emotes 403s and the picker silently
        // shows none of the operator's subscription emotes. (FeatureScopeMap still maps chat_emotes → this scope
        // so a stale token gets a NAMED gap to re-grant.)
        "user:read:emotes",
        "channel:read:subscriptions",
        "bits:read",
        "channel:manage:redemptions",
        "channel:read:redemptions",
        "channel:moderate",
        "moderator:manage:banned_users",
        "moderator:manage:chat_messages",
        "moderator:read:followers",
        "channel:manage:broadcast",
        "channel:read:polls",
        "channel:manage:polls",
        "channel:read:predictions",
        "channel:manage:predictions",
        "channel:read:hype_train",
        // Charity/Goals EventSub ingest (ROADMAP "Small decided items"): requested upfront alongside the
        // other per-topic read scopes above (channel:read:hype_train et al.) rather than gated behind a
        // feature toggle — this codebase requests the full per-channel scope set at login, not per-feature.
        // A streamer who never runs a charity campaign or creator goal simply never triggers these topics;
        // Twitch 403s the subscribe attempt gracefully if the scope is somehow missing (see BotLifecycleService).
        "channel:read:charity",
        "channel:read:goals",
        "channel:manage:schedule",
        "user:read:moderated_channels",
        // The remaining translator-backed EventSub surface (twitch-eventsub.md, BotLifecycleService
        // ChannelEventTypes): requested upfront alongside the scopes above rather than gated behind a feature
        // toggle, same rationale as the charity/goals block — a streamer who never triggers these Twitch
        // features simply never triggers their topic, and a missing scope 403s that one subscribe attempt
        // (TwitchEventSubHostedService.SubscribeAsync) without blocking any other topic.
        "channel:read:ads", // channel.ad_break.begin
        "channel:read:vips", // channel.vip.add / channel.vip.remove
        "moderation:read", // channel.moderator.add / channel.moderator.remove
        // Core proactive management jobs (roles-permissions §4) — these ride the base grant, not a feature toggle,
        // because they run unconditionally for every onboarded channel: the 10-minute ManagementRoleReconcileService
        // reads channel editors (Get Channel Editors) to map them to the dashboard Editor role, and
        // BotJoinOnOnboardingHandler makes the bot a channel moderator on join (Add Channel Moderator). Omitting
        // their scopes 403'd every reconcile/onboard cycle and nagged the streamer with a permission they could
        // never satisfy from a feature — so they belong here alongside the rest of the full per-channel set.
        "channel:read:editors", // ManagementRoleReconcileService → Get Channel Editors (Editor-role sync)
        "channel:manage:moderators", // BotJoinOnOnboardingHandler → Add Channel Moderator (bot self-mod on join)
        "moderator:manage:automod", // automod.message.hold/update, automod.terms.update
        "moderator:read:automod_settings", // automod.settings.update
        "moderator:read:blocked_terms", // channel.moderate v2
        "moderator:manage:blocked_terms", // dashboard blocked-terms editor (Add/Remove Blocked Term)
        "moderator:read:chat_settings", // channel.moderate v2
        "moderator:read:moderators", // channel.moderate v2
        "moderator:read:shield_mode", // channel.shield_mode.begin / channel.shield_mode.end
        "moderator:manage:shield_mode", // dashboard Shield Mode toggle (Update Shield Mode Status)
        "moderator:read:shoutouts", // channel.shoutout.create / channel.shoutout.receive
        "moderator:read:suspicious_users", // channel.suspicious_user.message / channel.suspicious_user.update
        "moderator:manage:suspicious_users", // dashboard suspicious-user flag (Update Suspicious User)
        "moderator:read:unban_requests", // channel.unban_request.create / channel.unban_request.resolve, channel.moderate v2
        "moderator:manage:unban_requests", // dashboard unban-request queue (Resolve Unban Request)
        "moderator:read:vips", // channel.moderate v2 (distinct from channel:read:vips above)
        "moderator:read:warnings", // channel.warning.acknowledge / channel.warning.send, channel.moderate v2
        "moderator:manage:warnings", // dashboard warn action (Warn Chat User)
        // user.whisper.message rides the BOT identity (BotScopes below) — this streamer-side grant is the
        // single-account self-host leg, where the streamer's own account IS the bot.
        "user:read:whispers", // user.whisper.message (single-account fallback)
        // Guest Star ingest (ROADMAP "Small decided items" — restored 2026-07-04; Twitch has not deprecated
        // the API, live docs still list all four beta topics). Read variants suffice for ingest-only —
        // channel:read:guest_star covers the broadcaster's own sessions, moderator:read:guest_star covers
        // sessions in channels where the bot moderates; both feed the same broadcaster+moderator condition
        // (EventSubConditionBuilder.ModeratorPlaneEvents).
        "channel:read:guest_star", // channel.guest_star_session.begin/.end, .guest.update, .settings.update
        "moderator:read:guest_star", // channel.guest_star_session.begin/.end, .guest.update, .settings.update
    ];

    private static readonly string[] BotScopes =
    [
        "user:read:chat",
        "user:write:chat",
        // The chatbot badge: a send on the platform's APP access token earns the bot its badge only when the
        // bot account has granted `user:bot`. With it (and bot-is-mod, which BotJoinOnOnboardingHandler ensures)
        // HelixChatProvider rides the badge-bearing app token; without it, it falls back to this user token.
        "user:bot",
        "chat:read",
        "chat:edit",
        // The bot's own whisper inbox: user.whisper.message is a bot-owned EventSub topic riding the bot's
        // token, so the scope must be granted on THIS identity — the streamer-side grant above only covers
        // the single-account self-host. Without it the platform-plane subscribe 403s (that one topic only).
        "user:read:whispers",
    ];

    public AuthService(
        IApplicationDbContext db,
        ITwitchAuthService twitchAuth,
        ITwitchDeviceCodeService deviceCode,
        IIntegrationTokenVault vault,
        ISessionService sessions,
        IEventBus eventBus,
        ISystemCredentialsProvider credentials,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        DeploymentContext deploymentContext,
        TimeProvider timeProvider,
        ILogger<AuthService> logger
    )
    {
        _db = db;
        _twitchAuth = twitchAuth;
        _deviceCode = deviceCode;
        _vault = vault;
        _sessions = sessions;
        _eventBus = eventBus;
        _credentials = credentials;
        _http = httpClientFactory.CreateClient("twitch-auth");
        _timeProvider = timeProvider;
        _logger = logger;
        _baseUrl = configuration["App:BaseUrl"] ?? "http://localhost:5080";
        _initialAdminTwitchId = configuration["App:InitialAdminTwitchId"];
        _isSelfHost = deploymentContext.IsSelfHost;
        _deploymentMode = deploymentContext.Mode;
    }

    // ─── User OAuth ──────────────────────────────────────────────────────────

    public Task<Result<string>> GetTwitchOAuthUrl(
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    ) =>
        BuildAuthorizeUrlAsync(
            RequiredScopes,
            state,
            baseUrl,
            forceVerify: false,
            cancellationToken
        );

    public async Task<Result<AuthResultDto>> HandleTwitchCallbackAsync(
        OAuthCallbackDto callback,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    )
    {
        string redirectUri = callback.RedirectUri ?? $"{_baseUrl}/api/v1/auth/twitch/callback";
        TokenResult? tokens = await _twitchAuth.ExchangeCodeAsync(
            callback.Code,
            redirectUri,
            cancellationToken
        );
        if (tokens is null)
            return Result.Failure<AuthResultDto>(
                "Failed to exchange authorization code.",
                "TOKEN_EXCHANGE_FAILED"
            );

        return await EstablishStreamerSessionAsync(tokens, context, cancellationToken);
    }

    /// <summary>
    /// Turn already-acquired streamer tokens into a live session: fetch the Twitch user, upsert the User +
    /// owning Channel, run the first-admin bootstrap, vault the tokens, fire the registration/onboarded events,
    /// and open a session + issue the JWT. Shared by the auth-code callback and the Device Code Flow poll, so
    /// both logins converge here. The connection is stamped with the client id alone — no secret, the only
    /// credential the no-secret device login has.
    /// </summary>
    private async Task<Result<AuthResultDto>> EstablishStreamerSessionAsync(
        TokenResult tokens,
        AuthContextDto context,
        CancellationToken cancellationToken
    )
    {
        TwitchUserInfo? twitchUser = await GetUserFromTokenAsync(
            tokens.AccessToken,
            cancellationToken
        );
        if (twitchUser is null)
            return Result.Failure<AuthResultDto>(
                "Failed to fetch Twitch user info.",
                "USER_FETCH_FAILED"
            );

        bool isNewUser = false;
        User? user = await _db.Users.FirstOrDefaultAsync(
            u => u.TwitchUserId == twitchUser.Id,
            cancellationToken
        );
        if (user is null)
        {
            isNewUser = true;
            user = new()
            {
                TwitchUserId = twitchUser.Id,
                Platform = AuthEnums.Platform.Twitch,
                Username = twitchUser.Login,
                UsernameNormalized = twitchUser.Login.ToLowerInvariant(),
                DisplayName = twitchUser.DisplayName,
                ProfileImageUrl = twitchUser.ProfileImageUrl,
                BroadcasterType = twitchUser.BroadcasterType,
                Type = twitchUser.Type,
                AccountCreatedAt = twitchUser.AccountCreatedAt,
                Enabled = true,
            };
            _db.Users.Add(user);
        }
        else
        {
            user.Username = twitchUser.Login;
            user.UsernameNormalized = twitchUser.Login.ToLowerInvariant();
            user.DisplayName = twitchUser.DisplayName;
            user.ProfileImageUrl = twitchUser.ProfileImageUrl;
            user.Type = twitchUser.Type; // revocable — re-read live each login
            if (twitchUser.AccountCreatedAt is not null)
                user.AccountCreatedAt = twitchUser.AccountCreatedAt; // immutable Twitch fact
        }
        user.LastSeenAt = _timeProvider.GetUtcNow().UtcDateTime;

        // Best-effort chat color sync (design-system §2): populate User.Color so the dashboard can derive the
        // dynamic accent from the streamer's Twitch chat color. Failures are swallowed — the login must still
        // succeed even when the /helix/chat/color call is unavailable or the scope is not yet granted.
        string? chatColor = await GetChatColorFromTokenAsync(
            tokens.AccessToken,
            twitchUser.Id,
            cancellationToken
        );
        if (chatColor is not null)
            user.Color = chatColor;

        // First-admin bootstrap (§12): the configured App:InitialAdminTwitchId match (any deployment), OR — on
        // self-host, where the owner IS the admin — the FIRST account to onboard when no platform principal
        // exists yet. No raw SQL, idempotent.
        bool anyPlatformPrincipalExists = await _db.Users.AnyAsync(
            u => u.IsPlatformPrincipal,
            cancellationToken
        );
        if (
            AdminBootstrap.ShouldPromote(
                user.IsPlatformPrincipal,
                _initialAdminTwitchId,
                user.TwitchUserId!,
                _isSelfHost,
                anyPlatformPrincipalExists
            )
        )
        {
            user.IsPlatformPrincipal = true;
            _logger.LogInformation(
                "Bootstrapped platform admin (self-host first owner or configured id): {TwitchUserId}",
                user.TwitchUserId
            );
        }

        // Keep the streamer's primary Twitch identity live + enriched from Helix (platform-identity §3.1).
        await PrimaryIdentityWriter.EnsureAsync(
            _db,
            _timeProvider,
            user.Id,
            AuthEnums.Platform.Twitch,
            twitchUser.Id,
            twitchUser.Login,
            twitchUser.DisplayName,
            twitchUser.ProfileImageUrl,
            cancellationToken: cancellationToken
        );

        await _db.SaveChangesAsync(cancellationToken);

        // Upsert the owning Channel (tenant root). A streamer's own channel = their Twitch user id.
        // SelfHostLite is single-streamer: a 2nd login reuses the existing owner's channel rather than
        // creating a second one. The new user's JWT will carry the owner's broadcasterId, giving them
        // viewer-level access to the single-channel dashboard.
        Channel? channel;
        if (_deploymentMode == DeploymentMode.SelfHostLite)
        {
            // In SelfHostLite, there is at most one channel. Use whichever exists (owner or first-onboarded),
            // creating one only if this is genuinely the very first login.
            channel =
                await _db
                    .Channels.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.OwnerUserId == user.Id, cancellationToken)
                ?? await _db
                    .Channels.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.IsOnboarded, cancellationToken);
        }
        else
        {
            // SelfHostFull / SaaS: every streamer owns their own channel.
            channel = await _db
                .Channels.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.OwnerUserId == user.Id, cancellationToken);
        }

        if (channel is null)
        {
            channel = new()
            {
                OwnerUserId = user.Id,
                TwitchChannelId = twitchUser.Id,
                ExternalChannelId = twitchUser.Id,
                Name = twitchUser.Login,
                NameNormalized = twitchUser.Login.ToLowerInvariant(),
                IsOnboarded = true,
            };
            _db.Channels.Add(channel);
            await _db.SaveChangesAsync(cancellationToken);
        }

        Guid broadcasterId = channel.Id;

        // Stamp the connection with the client id that owns it — read the id alone (the no-secret device login
        // has no secret; this is the shipped public id or a BYOC override).
        string? clientId = await _credentials.GetClientIdAsync(TwitchProvider, cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return Result.Failure<AuthResultDto>(
                "Twitch client id is not configured.",
                "TWITCH_NOT_CONFIGURED"
            );

        // Vault the user's Twitch tokens (replaces the flat Service row).
        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new UpsertConnectionDto(
                broadcasterId,
                AuthEnums.IntegrationProvider.Twitch,
                twitchUser.Id,
                twitchUser.Login,
                tokens.Scopes,
                clientId,
                IsByok: false,
                user.Id,
                SettingsJson: null
            ),
            cancellationToken
        );
        if (connection.IsSuccess)
            await _vault.StoreTokensAsync(
                connection.Value.Id,
                new StoreTokensDto(
                    tokens.AccessToken,
                    tokens.RefreshToken,
                    AppToken: null,
                    tokens.ExpiresAt
                ),
                tokens.Scopes,
                cancellationToken
            );

        if (isNewUser)
            await _eventBus.PublishAsync(
                new UserRegisteredEvent
                {
                    BroadcasterId = broadcasterId,
                    UserId = user.Id,
                    TwitchUserId = twitchUser.Id,
                    Username = twitchUser.Login,
                    Platform = AuthEnums.Platform.Twitch,
                },
                cancellationToken
            );

        // Re-published on EVERY successful streamer session — not just a brand-new channel. The auto-discovered
        // onboarding seed handlers (rewards, moderator roster, memberships, subscriber/VIP standing, channel
        // info, owner profile, event responses, banned-user import, bot mod-join, default commands, EventSub
        // subscribe) are all documented idempotent — the same guarantee OnboardedChannelSeedBackfillService
        // relies on to safely re-fire this event for every onboarded channel at startup — so re-auth of an
        // EXISTING channel is a safe repair path that keeps their seeded state in sync without a restart.
        await _eventBus.PublishAsync(
            new ChannelOnboardedEvent
            {
                BroadcasterId = broadcasterId,
                OwnerUserId = user.Id,
                TwitchChannelId = twitchUser.Id,
                Name = twitchUser.Login,
            },
            cancellationToken
        );

        // Open a session + issue the rotating refresh token + access JWT.
        Result<SessionTokensDto> session = await _sessions.CreateSessionAsync(
            user.Id,
            broadcasterId,
            context,
            cancellationToken
        );
        if (session.IsFailure)
            return session.WithValue<AuthResultDto>(null!);

        _logger.LogInformation("User {UserId} authenticated via Twitch OAuth", user.Id);
        return Result.Success(BuildAuthResult(session.Value, user));
    }

    // ─── Device Code Flow login (no client secret) ─────────────────────────────

    public Task<Result<DeviceCodeStartDto>> StartTwitchDeviceLoginAsync(
        CancellationToken cancellationToken = default
    ) => StartDeviceLoginAsync(RequiredScopes, cancellationToken);

    public Task<Result<DeviceCodeStartDto>> StartTwitchDeviceLoginForScopesAsync(
        IReadOnlyList<string> scopes,
        CancellationToken cancellationToken = default
    )
    {
        if (scopes.Count == 0)
            return Task.FromResult(
                Result.Failure<DeviceCodeStartDto>("No scopes requested.", "NO_SCOPES")
            );
        return StartDeviceLoginAsync([.. scopes], cancellationToken);
    }

    public Task<Result<DeviceCodeStartDto>> StartBotDeviceLoginAsync(
        CancellationToken cancellationToken = default
    ) => StartDeviceLoginAsync(BotScopes, cancellationToken);

    private async Task<Result<DeviceCodeStartDto>> StartDeviceLoginAsync(
        string[] scopes,
        CancellationToken cancellationToken
    )
    {
        DeviceCodeResult? code = await _deviceCode.RequestDeviceCodeAsync(
            scopes,
            cancellationToken
        );
        if (code is null)
            return Result.Failure<DeviceCodeStartDto>(
                "Twitch client id is not configured.",
                "TWITCH_NOT_CONFIGURED"
            );

        int expiresIn = (int)(code.ExpiresAt - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds;
        return Result.Success(
            new DeviceCodeStartDto(
                code.DeviceCode,
                code.UserCode,
                code.VerificationUri,
                code.Interval,
                expiresIn
            )
        );
    }

    public async Task<Result<DeviceLoginPollDto>> PollTwitchDeviceLoginAsync(
        string deviceCode,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    )
    {
        DevicePollOutcome outcome = await _deviceCode.PollOnceAsync(
            deviceCode,
            RequiredScopes,
            cancellationToken
        );
        if (outcome.Status != DevicePollStatus.Authorized || outcome.Tokens is null)
            return Result.Success(new DeviceLoginPollDto(MapPollStatus(outcome.Status)));

        Result<AuthResultDto> session = await EstablishStreamerSessionAsync(
            outcome.Tokens,
            context,
            cancellationToken
        );
        if (session.IsFailure)
            return session.WithValue<DeviceLoginPollDto>(null!);

        return Result.Success(new DeviceLoginPollDto(DeviceLoginStatus.Authorized, session.Value));
    }

    public async Task<Result<DeviceBotPollDto>> PollBotDeviceLoginAsync(
        string deviceCode,
        CancellationToken cancellationToken = default
    )
    {
        DevicePollOutcome outcome = await _deviceCode.PollOnceAsync(
            deviceCode,
            BotScopes,
            cancellationToken
        );
        if (outcome.Status != DevicePollStatus.Authorized || outcome.Tokens is null)
            return Result.Success(new DeviceBotPollDto(MapPollStatus(outcome.Status)));

        TwitchUserInfo? botUser = await GetUserFromTokenAsync(
            outcome.Tokens.AccessToken,
            cancellationToken
        );
        if (botUser is null)
            return Result.Failure<DeviceBotPollDto>(
                "Failed to fetch bot user info.",
                "USER_FETCH_FAILED"
            );

        Result<BotStatusDto> bot = await EstablishSharedBotAsync(
            botUser,
            outcome.Tokens,
            cancellationToken
        );
        if (bot.IsFailure)
            return bot.WithValue<DeviceBotPollDto>(null!);

        return Result.Success(new DeviceBotPollDto(DeviceLoginStatus.Authorized, bot.Value));
    }

    /// <summary>Map the transport's poll status to the wire string the client loops on.</summary>
    private static string MapPollStatus(DevicePollStatus status) =>
        status switch
        {
            DevicePollStatus.Pending => DeviceLoginStatus.Pending,
            DevicePollStatus.SlowDown => DeviceLoginStatus.SlowDown,
            DevicePollStatus.Expired => DeviceLoginStatus.Expired,
            DevicePollStatus.Denied => DeviceLoginStatus.Denied,
            _ => DeviceLoginStatus.Error,
        };

    public async Task<Result<AuthResultDto>> RefreshTokenAsync(
        string refreshToken,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    )
    {
        Result<SessionTokensDto> rotated = await _sessions.RotateAsync(
            refreshToken,
            context,
            cancellationToken
        );
        if (rotated.IsFailure)
            return rotated.WithValue<AuthResultDto>(null!);

        User? user = await _db
            .AuthSessions.Where(s => s.Id == rotated.Value.SessionId)
            .Select(s => s.User)
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
            return Result.Failure<AuthResultDto>("User not found.", "NOT_FOUND");

        return Result.Success(BuildAuthResult(rotated.Value, user));
    }

    public Task<Result> LogoutAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default
    ) => LogoutCoreAsync(userId, sessionId, cancellationToken);

    private async Task<Result> LogoutCoreAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken
    )
    {
        Result revoke = await _sessions.RevokeSessionAsync(
            sessionId,
            AuthEnums.RefreshTokenRevokedReason.Logout,
            cancellationToken
        );
        if (revoke.IsFailure)
            return revoke;

        await _eventBus.PublishAsync(
            new UserLoggedOutEvent
            {
                UserId = userId,
                SessionId = sessionId,
                Reason = AuthEnums.RefreshTokenRevokedReason.Logout,
            },
            cancellationToken
        );
        _logger.LogInformation(
            "User {UserId} logged out of session {SessionId}",
            userId,
            sessionId
        );
        return Result.Success();
    }

    public Task<Result<int>> LogoutAllAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    ) =>
        _sessions.RevokeAllForUserAsync(
            userId,
            AuthEnums.RefreshTokenRevokedReason.Logout,
            cancellationToken
        );

    // ─── Platform (shared) bot ─────────────────────────────────────────────────

    public Task<Result<string>> GetTwitchBotOAuthUrl(
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    ) => BuildAuthorizeUrlAsync(BotScopes, state, baseUrl, forceVerify: true, cancellationToken);

    public async Task<Result<BotStatusDto>> HandleTwitchBotCallbackAsync(
        OAuthCallbackDto callback,
        CancellationToken cancellationToken = default
    )
    {
        Result<(TwitchUserInfo Bot, TokenResult Tokens)> exchange = await ExchangeBotCodeAsync(
            callback,
            cancellationToken
        );
        if (exchange.IsFailure)
            return exchange.WithValue<BotStatusDto>(null!);
        (TwitchUserInfo botUser, TokenResult tokens) = exchange.Value;

        return await EstablishSharedBotAsync(botUser, tokens, cancellationToken);
    }

    /// <summary>
    /// Connect the shared platform bot from already-acquired bot tokens: upsert the BotAccount, vault the
    /// platform connection (BroadcasterId=null), and fire the authorized event. Shared by the auth-code bot
    /// callback and the Device Code Flow bot poll. Stamped with the client id alone (no secret).
    /// </summary>
    private async Task<Result<BotStatusDto>> EstablishSharedBotAsync(
        TwitchUserInfo botUser,
        TokenResult tokens,
        CancellationToken cancellationToken
    )
    {
        BotAccount bot = await UpsertBotAccountAsync(
            AuthEnums.BotIdentityType.Shared,
            botUser,
            cancellationToken
        );

        string? clientId = await _credentials.GetClientIdAsync(TwitchProvider, cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
            return Result.Failure<BotStatusDto>(
                "Twitch client id is not configured.",
                "TWITCH_NOT_CONFIGURED"
            );

        // Platform connection: BroadcasterId=null (shared across all channels).
        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new UpsertConnectionDto(
                BroadcasterId: null,
                PlatformBotProvider,
                botUser.Id,
                botUser.Login,
                tokens.Scopes,
                clientId,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            ),
            cancellationToken
        );
        if (connection.IsFailure)
            return connection.WithValue<BotStatusDto>(null!);

        bot.ConnectionId = connection.Value.Id;
        await _db.SaveChangesAsync(cancellationToken);

        await _vault.StoreTokensAsync(
            connection.Value.Id,
            new StoreTokensDto(
                tokens.AccessToken,
                tokens.RefreshToken,
                AppToken: null,
                tokens.ExpiresAt
            ),
            tokens.Scopes,
            cancellationToken
        );

        await _eventBus.PublishAsync(
            new BotAccountAuthorizedEvent
            {
                BroadcasterId = Guid.Empty,
                BotAccountId = bot.Id,
                IdentityType = AuthEnums.BotIdentityType.Shared,
                BotUsername = botUser.Login,
            },
            cancellationToken
        );

        _logger.LogInformation("Shared bot {BotLogin} connected via Twitch OAuth", botUser.Login);
        return Result.Success(
            new BotStatusDto(true, botUser.Login, botUser.DisplayName, botUser.ProfileImageUrl)
        );
    }

    public async Task<Result<BotStatusDto>> GetBotStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        BotAccount? bot = await _db
            .BotAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                b =>
                    b.IdentityType == AuthEnums.BotIdentityType.Shared
                    && b.IsActive
                    && b.DeletedAt == null,
                cancellationToken
            );
        if (bot?.ConnectionId is null)
            return Result.Success(new BotStatusDto(false, null, null, null));

        // The token must still decrypt for the bot to count as connected.
        Result<DecryptedTokenDto> access = await _vault.GetAccessTokenAsync(
            bot.ConnectionId.Value,
            cancellationToken
        );
        if (access.IsFailure)
            return Result.Success(new BotStatusDto(false, null, null, null));

        return Result.Success(new BotStatusDto(true, bot.BotUsername, bot.BotUsername, null));
    }

    public async Task<Result> DisconnectBotAsync(CancellationToken cancellationToken = default)
    {
        BotAccount? bot = await _db
            .BotAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                b => b.IdentityType == AuthEnums.BotIdentityType.Shared && b.DeletedAt == null,
                cancellationToken
            );
        if (bot is null)
            return Result.Success();

        if (bot.ConnectionId is not null)
            await _vault.RevokeConnectionAsync(
                bot.ConnectionId.Value,
                "bot_disconnect",
                cancellationToken
            );

        bot.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);

        await _eventBus.PublishAsync(
            new BotAccountDisconnectedEvent
            {
                BroadcasterId = Guid.Empty,
                BotAccountId = bot.Id,
                Reason = "bot_disconnect",
            },
            cancellationToken
        );
        _logger.LogInformation("Shared bot disconnected");
        return Result.Success();
    }

    // ─── Custom (white-label) per-channel bot ──────────────────────────────────

    public Task<Result<string>> GetTwitchChannelBotOAuthUrl(
        Guid broadcasterId,
        string? state = null,
        string? baseUrl = null,
        CancellationToken cancellationToken = default
    ) => BuildAuthorizeUrlAsync(BotScopes, state, baseUrl, forceVerify: true, cancellationToken);

    public async Task<Result<BotStatusDto>> HandleTwitchChannelBotCallbackAsync(
        Guid broadcasterId,
        OAuthCallbackDto callback,
        CancellationToken cancellationToken = default
    )
    {
        Result<(TwitchUserInfo Bot, TokenResult Tokens)> exchange = await ExchangeBotCodeAsync(
            callback,
            cancellationToken
        );
        if (exchange.IsFailure)
            return exchange.WithValue<BotStatusDto>(null!);
        (TwitchUserInfo botUser, TokenResult tokens) = exchange.Value;

        BotAccount bot = await UpsertBotAccountAsync(
            AuthEnums.BotIdentityType.Custom,
            botUser,
            cancellationToken
        );

        SystemAppCredentials? app = await _credentials.GetAsync(TwitchProvider, cancellationToken);
        if (app is null)
            return Result.Failure<BotStatusDto>(
                "Twitch app credentials are not configured.",
                "TWITCH_NOT_CONFIGURED"
            );

        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new UpsertConnectionDto(
                broadcasterId,
                PlatformBotProvider,
                botUser.Id,
                botUser.Login,
                tokens.Scopes,
                app.ClientId,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            ),
            cancellationToken
        );
        if (connection.IsFailure)
            return connection.WithValue<BotStatusDto>(null!);

        bot.ConnectionId = connection.Value.Id;

        ChannelBotAuthorization? authorization = await _db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                a => a.BroadcasterId == broadcasterId && a.BotAccountId == bot.Id,
                cancellationToken
            );
        if (authorization is null)
        {
            authorization = new()
            {
                BroadcasterId = broadcasterId,
                BotAccountId = bot.Id,
                AuthorizedAt = _timeProvider.GetUtcNow().UtcDateTime,
                IsActive = true,
            };
            _db.ChannelBotAuthorizations.Add(authorization);
        }
        else
        {
            authorization.IsActive = true;
            authorization.DeletedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _vault.StoreTokensAsync(
            connection.Value.Id,
            new StoreTokensDto(
                tokens.AccessToken,
                tokens.RefreshToken,
                AppToken: null,
                tokens.ExpiresAt
            ),
            tokens.Scopes,
            cancellationToken
        );

        await _eventBus.PublishAsync(
            new BotAccountAuthorizedEvent
            {
                BroadcasterId = broadcasterId,
                BotAccountId = bot.Id,
                IdentityType = AuthEnums.BotIdentityType.Custom,
                BotUsername = botUser.Login,
            },
            cancellationToken
        );

        _logger.LogInformation(
            "Custom bot {BotLogin} connected for channel {ChannelId}",
            botUser.Login,
            broadcasterId
        );
        return Result.Success(
            new BotStatusDto(true, botUser.Login, botUser.DisplayName, botUser.ProfileImageUrl)
        );
    }

    public async Task<Result<BotStatusDto>> GetChannelBotStatusAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelBotAuthorization? authorization = await _db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                a => a.BroadcasterId == broadcasterId && a.IsActive && a.DeletedAt == null,
                cancellationToken
            );
        if (authorization is null)
            return Result.Success(new BotStatusDto(false, null, null, null));

        BotAccount? bot = await _db
            .BotAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == authorization.BotAccountId, cancellationToken);
        if (bot?.ConnectionId is null)
            return Result.Success(new BotStatusDto(false, null, null, null));

        Result<DecryptedTokenDto> access = await _vault.GetAccessTokenAsync(
            bot.ConnectionId.Value,
            cancellationToken
        );
        if (access.IsFailure)
            return Result.Success(new BotStatusDto(false, null, null, null));

        return Result.Success(new BotStatusDto(true, bot.BotUsername, bot.BotUsername, null));
    }

    public async Task<Result> DisconnectChannelBotAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelBotAuthorization? authorization = await _db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                a => a.BroadcasterId == broadcasterId && a.DeletedAt == null,
                cancellationToken
            );
        if (authorization is null)
            return Result.Success();

        authorization.IsActive = false;
        authorization.DeletedAt = _timeProvider.GetUtcNow().UtcDateTime;

        BotAccount? bot = await _db
            .BotAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == authorization.BotAccountId, cancellationToken);
        if (bot?.ConnectionId is not null)
            await _vault.RevokeConnectionAsync(
                bot.ConnectionId.Value,
                "channel_bot_disconnect",
                cancellationToken
            );

        await _db.SaveChangesAsync(cancellationToken);

        if (bot is not null)
            await _eventBus.PublishAsync(
                new BotAccountDisconnectedEvent
                {
                    BroadcasterId = broadcasterId,
                    BotAccountId = bot.Id,
                    Reason = "channel_bot_disconnect",
                },
                cancellationToken
            );

        _logger.LogInformation("Custom bot disconnected for channel {ChannelId}", broadcasterId);
        return Result.Success();
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Result<string>> BuildAuthorizeUrlAsync(
        string[] scopes,
        string? state,
        string? baseUrl,
        bool forceVerify,
        CancellationToken cancellationToken
    )
    {
        SystemAppCredentials? app = await _credentials.GetAsync(TwitchProvider, cancellationToken);
        if (app is null)
            return Result.Failure<string>(
                "Twitch app credentials are not configured. Finish setup before starting an OAuth flow.",
                "TWITCH_NOT_CONFIGURED"
            );

        string publicBaseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? _baseUrl : baseUrl).TrimEnd(
            '/'
        );
        string clientId = Uri.EscapeDataString(app.ClientId);
        string scope = Uri.EscapeDataString(string.Join(' ', scopes));
        string redirectUri = Uri.EscapeDataString($"{publicBaseUrl}/api/v1/auth/twitch/callback");
        string stateParam = state is not null
            ? $"&state={Uri.EscapeDataString(state)}"
            : string.Empty;
        string verify = forceVerify ? "&force_verify=true" : string.Empty;

        return Result.Success(
            "https://id.twitch.tv/oauth2/authorize"
                + $"?client_id={clientId}"
                + $"&redirect_uri={redirectUri}"
                + "&response_type=code"
                + $"&scope={scope}"
                + verify
                + stateParam
        );
    }

    private async Task<Result<(TwitchUserInfo, TokenResult)>> ExchangeBotCodeAsync(
        OAuthCallbackDto callback,
        CancellationToken cancellationToken
    )
    {
        string redirectUri = callback.RedirectUri ?? $"{_baseUrl}/api/v1/auth/twitch/callback";
        TokenResult? tokens = await _twitchAuth.ExchangeCodeAsync(
            callback.Code,
            redirectUri,
            cancellationToken
        );
        if (tokens is null)
            return Result.Failure<(TwitchUserInfo, TokenResult)>(
                "Failed to exchange authorization code.",
                "TOKEN_EXCHANGE_FAILED"
            );

        TwitchUserInfo? botUser = await GetUserFromTokenAsync(
            tokens.AccessToken,
            cancellationToken
        );
        if (botUser is null)
            return Result.Failure<(TwitchUserInfo, TokenResult)>(
                "Failed to fetch bot user info.",
                "USER_FETCH_FAILED"
            );

        return Result.Success((botUser, tokens));
    }

    private async Task<BotAccount> UpsertBotAccountAsync(
        string identityType,
        TwitchUserInfo botUser,
        CancellationToken cancellationToken
    )
    {
        BotAccount? bot = await _db
            .BotAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.BotUserId == botUser.Id, cancellationToken);
        if (bot is null)
        {
            bot = new()
            {
                IdentityType = identityType,
                Platform = AuthEnums.Platform.Twitch,
                BotUserId = botUser.Id,
                BotUsername = botUser.Login,
                IsActive = true,
            };
            _db.BotAccounts.Add(bot);
        }
        else
        {
            bot.BotUsername = botUser.Login;
            bot.IsActive = true;
            bot.DeletedAt = null;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return bot;
    }

    private AuthResultDto BuildAuthResult(SessionTokensDto session, User user)
    {
        UserDto userDto = new(
            user.Id.ToString(),
            user.Username,
            user.DisplayName,
            user.ProfileImageUrl,
            null,
            user.CreatedAt,
            user.UpdatedAt
        );
        return new AuthResultDto(
            session.AccessToken,
            session.RawRefreshToken,
            session.AccessExpiresAt,
            userDto
        );
    }

    private async Task<TwitchUserInfo?> GetUserFromTokenAsync(
        string accessToken,
        CancellationToken ct
    )
    {
        string? clientId = await _credentials.GetClientIdAsync(TwitchProvider, ct);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogWarning("Cannot fetch Twitch user: no client id is configured.");
            return null;
        }

        HttpRequestMessage request = new(HttpMethod.Get, "https://api.twitch.tv/helix/users");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("Client-Id", clientId);

        try
        {
            HttpResponseMessage response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // Log WHY so a "Failed to fetch Twitch user info." login failure is diagnosable instead of a
                // silent null: the Helix status + body (e.g. "invalid OAuth token") and the client-id suffix,
                // which surfaces a token/Client-Id client mismatch.
                string detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Twitch GET /helix/users returned {Status} (Client-Id …{ClientIdSuffix}): {Detail}",
                    (int)response.StatusCode,
                    clientId.Length >= 4 ? clientId[^4..] : clientId,
                    detail.Length > 300 ? detail[..300] : detail
                );

                return null;
            }

            HelixDataResponse<HelixUser>? data = await response.Content.ReadFromJsonAsync<
                HelixDataResponse<HelixUser>
            >(cancellationToken: ct);
            HelixUser? user = data?.Data?.FirstOrDefault();
            if (user is null)
                return null;

            return new(
                user.Id,
                user.Login,
                user.DisplayName,
                user.ProfileImageUrl,
                user.BroadcasterType,
                user.Type,
                user.CreatedAt
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to fetch current Twitch user from token");
            return null;
        }
    }

    /// <summary>
    /// Fetches the Twitch user's chat name color from <c>GET /helix/chat/color</c> using their user token.
    /// Returns <c>null</c> (never throws) — callers must treat this as best-effort decoration only.
    /// </summary>
    private async Task<string?> GetChatColorFromTokenAsync(
        string accessToken,
        string twitchUserId,
        CancellationToken ct
    )
    {
        try
        {
            string? clientId = await _credentials.GetClientIdAsync(TwitchProvider, ct);
            if (string.IsNullOrWhiteSpace(clientId))
                return null;

            HttpRequestMessage request = new(
                HttpMethod.Get,
                $"https://api.twitch.tv/helix/chat/color?user_id={twitchUserId}"
            );
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.Add("Client-Id", clientId);

            HttpResponseMessage response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            HelixDataResponse<HelixChatColor>? data = await response.Content.ReadFromJsonAsync<
                HelixDataResponse<HelixChatColor>
            >(cancellationToken: ct);

            string? color = data?.Data?.FirstOrDefault()?.Color;
            return string.IsNullOrWhiteSpace(color) ? null : color;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Best-effort chat color fetch failed; continuing without it");
            return null;
        }
    }

    private sealed class HelixDataResponse<T>
    {
        [JsonPropertyName("data")]
        public List<T>? Data { get; set; }
    }

    private sealed class HelixChatColor
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = null!;

        [JsonPropertyName("color")]
        public string? Color { get; set; }
    }

    private sealed class HelixUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = null!;

        [JsonPropertyName("login")]
        public string Login { get; set; } = null!;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = null!;

        [JsonPropertyName("profile_image_url")]
        public string? ProfileImageUrl { get; set; }

        [JsonPropertyName("broadcaster_type")]
        public string BroadcasterType { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
