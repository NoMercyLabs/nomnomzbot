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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

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

    private static readonly string[] RequiredScopes =
    [
        "user:read:email",
        "user:read:chat",
        "channel:read:subscriptions",
        "bits:read",
        "channel:manage:redemptions",
        "channel:read:redemptions",
        "moderator:manage:banned_users",
        "moderator:manage:chat_messages",
        "moderator:read:followers",
        "channel:manage:broadcast",
        "channel:read:polls",
        "channel:manage:polls",
        "channel:read:predictions",
        "channel:manage:predictions",
    ];

    private static readonly string[] BotScopes =
    [
        "user:read:chat",
        "user:write:chat",
        "chat:read",
        "chat:edit",
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
        _http = httpClientFactory.CreateClient("twitch-helix");
        _timeProvider = timeProvider;
        _logger = logger;
        _baseUrl = configuration["App:BaseUrl"] ?? "http://localhost:5080";
        _initialAdminTwitchId = configuration["App:InitialAdminTwitchId"];
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

        // First-admin bootstrap (§12): a self-hoster sets App:InitialAdminTwitchId to their Twitch user id,
        // and the matching account is promoted to platform principal on login — no raw SQL, idempotent.
        if (
            AdminBootstrap.ShouldPromote(
                user.IsPlatformPrincipal,
                _initialAdminTwitchId,
                user.TwitchUserId
            )
        )
        {
            user.IsPlatformPrincipal = true;
            _logger.LogInformation(
                "Bootstrapped platform admin from App:InitialAdminTwitchId: {TwitchUserId}",
                user.TwitchUserId
            );
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Upsert the owning Channel (tenant root). A streamer's own channel = their Twitch user id.
        bool isNewChannel = false;
        Channel? channel = await _db
            .Channels.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.OwnerUserId == user.Id, cancellationToken);
        if (channel is null)
        {
            isNewChannel = true;
            channel = new()
            {
                OwnerUserId = user.Id,
                TwitchChannelId = twitchUser.Id,
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
        if (isNewChannel)
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
                return null;

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

    private sealed class HelixDataResponse<T>
    {
        [JsonPropertyName("data")]
        public List<T>? Data { get; set; }
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
