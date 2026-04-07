// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) NoMercy Entertainment. All rights reserved.

using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoMercyBot.Application.Common.Interfaces;
using NoMercyBot.Application.Common.Models;
using NoMercyBot.Application.Contracts.Twitch;
using NoMercyBot.Application.DTOs.Auth;
using NoMercyBot.Application.DTOs.Users;
using NoMercyBot.Application.Services;
using NoMercyBot.Domain.Entities;
using NoMercyBot.Infrastructure.Configuration;

namespace NoMercyBot.Infrastructure.Services.Application;

/// <summary>
/// Handles Twitch OAuth authentication:
/// 1. Redirect to Twitch authorization URL
/// 2. Exchange code for tokens
/// 3. Upsert user record
/// 4. Issue a platform JWT
/// 5. Store Twitch tokens encrypted in Service table
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly ITwitchAuthService _twitchAuth;
    private readonly IJwtTokenService _jwt;
    private readonly IEncryptionService _encryption;
    private readonly HttpClient _http;
    private readonly TwitchOptions _options;
    private readonly ILogger<AuthService> _logger;
    private readonly string _baseUrl;

    private static readonly string[] RequiredScopes =
    [
        // Identity
        "user:read:email",
        "user:read:chat",
        // Chat (IRC)
        "chat:read",
        "chat:edit",
        // Subscriptions & bits
        "channel:read:subscriptions",
        "bits:read",
        // Channel points
        "channel:manage:redemptions",
        "channel:read:redemptions",
        // Moderation
        "moderator:read:chatters",
        "moderator:manage:banned_users",
        "moderator:manage:chat_messages",
        "moderator:manage:chat_settings",
        "moderator:read:followers",
        "channel:moderate",
        "channel:manage:moderators",
        // VIPs
        "channel:read:vips",
        "channel:manage:vips",
        // Stream management
        "channel:manage:broadcast",
        "channel:read:polls",
        "channel:manage:polls",
        "channel:read:predictions",
        "channel:manage:predictions",
        // Moderated channels (for channel switcher)
        "moderation:read",
    ];

    private static readonly string[] BotScopes =
    [
        // Identity
        "user:read:email",
        // Chat
        "user:read:chat",
        "user:write:chat",
        "chat:read",
        "chat:edit",
        "whispers:read",
        "whispers:edit",
        // Moderation (bot must be a mod in the channel for these to work)
        "moderator:read:chatters",
        "moderator:manage:banned_users",
        "moderator:manage:chat_messages",
        "moderator:manage:chat_settings",
        "moderator:read:followers",
        // Channel management
        "channel:read:subscriptions",
        "channel:manage:redemptions",
        "channel:read:redemptions",
        "channel:manage:broadcast",
        "channel:read:polls",
        "channel:manage:polls",
        "channel:read:predictions",
        "channel:manage:predictions",
    ];

    public AuthService(
        IApplicationDbContext db,
        ITwitchAuthService twitchAuth,
        IJwtTokenService jwt,
        IEncryptionService encryption,
        IHttpClientFactory httpClientFactory,
        IOptions<TwitchOptions> options,
        IConfiguration configuration,
        ILogger<AuthService> logger
    )
    {
        _db = db;
        _twitchAuth = twitchAuth;
        _jwt = jwt;
        _encryption = encryption;
        _http = httpClientFactory.CreateClient("twitch-helix");
        _options = options.Value;
        _logger = logger;
        _baseUrl = configuration["App:BaseUrl"] ?? "http://localhost:5080";
    }

    public async Task<string> GetTwitchOAuthUrl(string? state = null, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        string publicBaseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? _baseUrl : baseUrl).TrimEnd('/');
        string clientId = Uri.EscapeDataString(await GetEffectiveClientIdAsync(cancellationToken));
        string scopes = Uri.EscapeDataString(string.Join(" ", RequiredScopes));
        string redirectUri = Uri.EscapeDataString($"{publicBaseUrl}/api/v1/auth/twitch/callback");
        string stateParam = state is not null
            ? $"&state={Uri.EscapeDataString(state)}"
            : string.Empty;

        return $"https://id.twitch.tv/oauth2/authorize"
            + $"?client_id={clientId}"
            + $"&redirect_uri={redirectUri}"
            + $"&response_type=code"
            + $"&scope={scopes}"
            + stateParam;
    }

    public async Task<Result<AuthResultDto>> HandleTwitchCallbackAsync(
        OAuthCallbackDto callback,
        CancellationToken cancellationToken = default
    )
    {
        // Exchange code for tokens.
        // Mobile clients send their own redirect URI (e.g. nomercybot://callback) which
        // must match what was used in the authorization request.
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

        // Fetch Twitch user info using the fresh access token (returns authenticated user, no id query)
        TwitchUserInfo? twitchUser = await GetUserFromTokenAsync(
            tokens.AccessToken,
            cancellationToken
        );
        if (twitchUser is null)
            return Result.Failure<AuthResultDto>(
                "Failed to fetch Twitch user info.",
                "USER_FETCH_FAILED"
            );

        // Upsert user
        User? user = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == twitchUser.Id,
            cancellationToken
        );

        if (user is null)
        {
            user = new()
            {
                Id = twitchUser.Id,
                Username = twitchUser.Login,
                DisplayName = twitchUser.DisplayName,
                ProfileImageUrl = twitchUser.ProfileImageUrl,
                BroadcasterType = twitchUser.BroadcasterType,
                Enabled = true,
            };
            _db.Users.Add(user);
        }
        else
        {
            user.Username = twitchUser.Login;
            user.DisplayName = twitchUser.DisplayName;
            user.ProfileImageUrl = twitchUser.ProfileImageUrl;
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Store Twitch tokens in Service table.
        // Use BroadcasterId=null if no channel exists yet — will be linked after onboarding.
        bool channelExists = await _db.Channels.AnyAsync(
            c => c.Id == twitchUser.Id,
            cancellationToken
        );

        string? broadcasterId = channelExists ? twitchUser.Id : null;

        Service? service = await _db.Services.FirstOrDefaultAsync(
            s => (s.BroadcasterId == twitchUser.Id || (s.BroadcasterId == null && s.UserId == twitchUser.Id))
                 && s.Name == "twitch",
            cancellationToken
        );

        if (service is null)
        {
            service = new()
            {
                Name = "twitch",
                BroadcasterId = broadcasterId,
                UserId = twitchUser.Id,
                Enabled = true,
            };
            _db.Services.Add(service);
        }

        service.AccessToken = _encryption.Encrypt(tokens.AccessToken);
        service.RefreshToken = _encryption.Encrypt(tokens.RefreshToken);
        service.TokenExpiry = tokens.ExpiresAt;
        service.Scopes = tokens.Scopes;
        // Link to channel if it now exists
        if (channelExists && service.BroadcasterId is null)
            service.BroadcasterId = twitchUser.Id;

        await _db.SaveChangesAsync(cancellationToken);

        // Issue platform JWT
        IEnumerable<string> roles = user.IsAdmin ? ["user", "admin"] : ["user"];
        string platformJwt = _jwt.GenerateToken(twitchUser.Id, twitchUser.Login, roles);
        string refreshJwt = _jwt.GenerateRefreshToken(twitchUser.Id, twitchUser.Login);

        UserDto userDto = new(
            twitchUser.Id,
            twitchUser.Login,
            twitchUser.DisplayName,
            twitchUser.ProfileImageUrl,
            null,
            user.CreatedAt,
            user.UpdatedAt
        );

        _logger.LogInformation("User {UserId} authenticated via Twitch OAuth", twitchUser.Id);

        return Result.Success(
            new AuthResultDto(platformJwt, refreshJwt, DateTime.UtcNow.AddHours(1), userDto)
        );
    }

    public async Task<Result<AuthResultDto>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default
    )
    {
        ClaimsPrincipal? principal = _jwt.ValidateToken(refreshToken);
        if (principal is null)
            return Result.Failure<AuthResultDto>(
                "Invalid or expired refresh token.",
                "INVALID_TOKEN"
            );

        string? userId = principal
            .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?.Value;
        if (userId is null)
            return Result.Failure<AuthResultDto>("Token missing user ID.", "INVALID_TOKEN");

        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return Result.Failure<AuthResultDto>("User not found.", "NOT_FOUND");

        IEnumerable<string> refreshRoles = user.IsAdmin ? ["user", "admin"] : ["user"];
        string newJwt = _jwt.GenerateToken(user.Id, user.Username, refreshRoles);
        string newRefresh = _jwt.GenerateRefreshToken(user.Id, user.Username);

        UserDto userDto = new(
            user.Id,
            user.Username,
            user.DisplayName,
            user.ProfileImageUrl,
            null,
            user.CreatedAt,
            user.UpdatedAt
        );

        return Result.Success(
            new AuthResultDto(newJwt, newRefresh, DateTime.UtcNow.AddHours(1), userDto)
        );
    }

    public async Task<Result> LogoutAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await _twitchAuth.RevokeTokenAsync(userId, "twitch", cancellationToken);
            _logger.LogInformation("User {UserId} logged out", userId);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during logout for user {UserId}", userId);
            return Result.Failure("Logout failed.", "LOGOUT_FAILED");
        }
    }

    // ─── Bot account ─────────────────────────────────────────────────────────

    public async Task<string> GetTwitchBotOAuthUrl(string? state = null, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        string publicBaseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? _baseUrl : baseUrl).TrimEnd('/');
        string clientId = Uri.EscapeDataString(await GetEffectiveClientIdAsync(cancellationToken));
        string scopes = Uri.EscapeDataString(string.Join(" ", BotScopes));
        string redirectUri = Uri.EscapeDataString($"{publicBaseUrl}/api/v1/auth/twitch/callback");
        string stateParam = state is not null
            ? $"&state={Uri.EscapeDataString(state)}"
            : string.Empty;

        return $"https://id.twitch.tv/oauth2/authorize"
            + $"?client_id={clientId}"
            + $"&redirect_uri={redirectUri}"
            + $"&response_type=code"
            + $"&scope={scopes}"
            + $"&force_verify=true"
            + stateParam;
    }

    public async Task<Result<BotStatusDto>> HandleTwitchBotCallbackAsync(
        OAuthCallbackDto callback,
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
            return Result.Failure<BotStatusDto>("Failed to exchange authorization code.", "TOKEN_EXCHANGE_FAILED");

        TwitchUserInfo? botUser = await GetUserFromTokenAsync(tokens.AccessToken, cancellationToken);
        if (botUser is null)
            return Result.Failure<BotStatusDto>("Failed to fetch bot user info.", "USER_FETCH_FAILED");

        // Upsert the bot's User record so we can display login/displayName later
        User? botUserRecord = await _db.Users.FirstOrDefaultAsync(u => u.Id == botUser.Id, cancellationToken);
        if (botUserRecord is null)
        {
            botUserRecord = new()
            {
                Id = botUser.Id,
                Username = botUser.Login,
                DisplayName = botUser.DisplayName,
                ProfileImageUrl = botUser.ProfileImageUrl,
                BroadcasterType = botUser.BroadcasterType,
                Enabled = true,
            };
            _db.Users.Add(botUserRecord);
        }
        else
        {
            botUserRecord.Username = botUser.Login;
            botUserRecord.DisplayName = botUser.DisplayName;
            botUserRecord.ProfileImageUrl = botUser.ProfileImageUrl;
        }

        // Upsert the bot Service record — BroadcasterId=null (shared across all channels)
        Service? service = await _db.Services.FirstOrDefaultAsync(
            s => s.Name == "twitch_bot" && s.BroadcasterId == null,
            cancellationToken
        );

        if (service is null)
        {
            service = new()
            {
                Name = "twitch_bot",
                BroadcasterId = null,
                UserId = botUser.Id,
                Enabled = true,
            };
            _db.Services.Add(service);
        }

        service.AccessToken = _encryption.Encrypt(tokens.AccessToken);
        service.RefreshToken = _encryption.Encrypt(tokens.RefreshToken);
        service.TokenExpiry = tokens.ExpiresAt;
        service.Scopes = tokens.Scopes;
        service.UserId = botUser.Id;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Bot account {BotLogin} connected via Twitch OAuth", botUser.Login);

        return Result.Success(new BotStatusDto(true, botUser.Login, botUser.DisplayName, botUser.ProfileImageUrl));
    }

    public async Task<Result<BotStatusDto>> GetBotStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        Service? service = await _db.Services
            .Where(s => s.Name == "twitch_bot" && s.BroadcasterId == null && s.Enabled && s.AccessToken != null)
            .FirstOrDefaultAsync(cancellationToken);

        if (service is null)
            return Result.Success(new BotStatusDto(false, null, null, null));

        // If the token cannot be decrypted (e.g. encryption key changed), report disconnected
        // so the admin UI shows the re-auth button rather than hiding it.
        if (_encryption.TryDecrypt(service.AccessToken) is null)
        {
            _logger.LogWarning("Platform bot token exists but cannot be decrypted — reporting as disconnected");
            return Result.Success(new BotStatusDto(false, null, null, null));
        }

        // Optionally resolve the bot user display info via UserId
        if (service.UserId is not null)
        {
            User? botUser = await _db.Users.FirstOrDefaultAsync(
                u => u.Id == service.UserId, cancellationToken);
            if (botUser is not null)
                return Result.Success(new BotStatusDto(true, botUser.Username, botUser.DisplayName, botUser.ProfileImageUrl));
        }

        // Fall back to the configured bot username so the UI always shows something
        return Result.Success(new BotStatusDto(true, _options.BotUsername, _options.BotUsername, null));
    }

    public async Task<Result> DisconnectBotAsync(CancellationToken cancellationToken = default)
    {
        Service? service = await _db.Services
            .FirstOrDefaultAsync(s => s.Name == "twitch_bot" && s.BroadcasterId == null, cancellationToken);

        if (service is null)
            return Result.Success();

        try
        {
            if (service.AccessToken is not null)
            {
                string? decrypted = _encryption.TryDecrypt(service.AccessToken);
                if (decrypted is not null)
                    await _twitchAuth.RevokeTokenAsync(service.UserId ?? "", "twitch_bot", cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to revoke bot token from Twitch — removing locally anyway");
        }

        _db.Services.Remove(service);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Bot account disconnected");
        return Result.Success();
    }

    // ─── White-label per-channel bot ─────────────────────────────────────────

    public async Task<string> GetTwitchChannelBotOAuthUrl(string channelId, string? state = null, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        string publicBaseUrl = (string.IsNullOrWhiteSpace(baseUrl) ? _baseUrl : baseUrl).TrimEnd('/');
        string clientId = Uri.EscapeDataString(await GetEffectiveClientIdAsync(cancellationToken));
        string scopes = Uri.EscapeDataString(string.Join(" ", BotScopes));
        string redirectUri = Uri.EscapeDataString($"{publicBaseUrl}/api/v1/auth/twitch/callback");

        // Embed the flow type + channelId so the single callback endpoint can route it.
        var payload = new { flow = "channel_bot", channel_id = channelId, redirect_uri = state };
        string encodedState = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)));

        return $"https://id.twitch.tv/oauth2/authorize"
            + $"?client_id={clientId}"
            + $"&redirect_uri={redirectUri}"
            + $"&response_type=code"
            + $"&scope={scopes}"
            + $"&force_verify=true"
            + $"&state={Uri.EscapeDataString(encodedState)}";
    }

    public async Task<Result<BotStatusDto>> HandleTwitchChannelBotCallbackAsync(
        string channelId,
        OAuthCallbackDto callback,
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
            return Result.Failure<BotStatusDto>("Failed to exchange authorization code.", "TOKEN_EXCHANGE_FAILED");

        TwitchUserInfo? botUser = await GetUserFromTokenAsync(tokens.AccessToken, cancellationToken);
        if (botUser is null)
            return Result.Failure<BotStatusDto>("Failed to fetch bot user info.", "USER_FETCH_FAILED");

        // Store per-channel: Name="twitch_bot", BroadcasterId=channelId
        Service? service = await _db.Services.FirstOrDefaultAsync(
            s => s.Name == "twitch_bot" && s.BroadcasterId == channelId,
            cancellationToken
        );

        if (service is null)
        {
            service = new()
            {
                Name = "twitch_bot",
                BroadcasterId = channelId,
                UserId = botUser.Id,
                Enabled = true,
            };
            _db.Services.Add(service);
        }

        service.AccessToken = _encryption.Encrypt(tokens.AccessToken);
        service.RefreshToken = _encryption.Encrypt(tokens.RefreshToken);
        service.TokenExpiry = tokens.ExpiresAt;
        service.Scopes = tokens.Scopes;
        service.UserId = botUser.Id;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "White-label bot {BotLogin} connected for channel {ChannelId}",
            botUser.Login, channelId);

        return Result.Success(new BotStatusDto(true, botUser.Login, botUser.DisplayName, botUser.ProfileImageUrl));
    }

    public async Task<Result<BotStatusDto>> GetChannelBotStatusAsync(
        string channelId,
        CancellationToken cancellationToken = default
    )
    {
        Service? service = await _db.Services
            .Where(s => s.Name == "twitch_bot" && s.BroadcasterId == channelId && s.Enabled && s.AccessToken != null)
            .FirstOrDefaultAsync(cancellationToken);

        if (service is null)
            return Result.Success(new BotStatusDto(false, null, null, null));

        if (_encryption.TryDecrypt(service.AccessToken) is null)
            return Result.Success(new BotStatusDto(false, null, null, null));

        if (service.UserId is not null)
        {
            User? botUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == service.UserId, cancellationToken);
            if (botUser is not null)
                return Result.Success(new BotStatusDto(true, botUser.Username, botUser.DisplayName, botUser.ProfileImageUrl));
        }

        return Result.Success(new BotStatusDto(true, null, null, null));
    }

    public async Task<Result> DisconnectChannelBotAsync(string channelId, CancellationToken cancellationToken = default)
    {
        Service? service = await _db.Services
            .FirstOrDefaultAsync(s => s.Name == "twitch_bot" && s.BroadcasterId == channelId, cancellationToken);

        if (service is null)
            return Result.Success();

        _db.Services.Remove(service);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("White-label bot disconnected for channel {ChannelId}", channelId);
        return Result.Success();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Twitch Client ID, preferring the value stored in the system configuration
    /// table (set via the setup wizard) and falling back to the static options value.
    /// </summary>
    private async Task<string> GetEffectiveClientIdAsync(CancellationToken ct)
    {
        Domain.Entities.Configuration? cfg = await _db.Configurations
            .FirstOrDefaultAsync(c => c.BroadcasterId == null && c.Key == "twitch.client_id", ct);
        string? dbClientId = cfg?.SecureValue ?? cfg?.Value;
        return !string.IsNullOrWhiteSpace(dbClientId) ? dbClientId : _options.ClientId;
    }

    /// <summary>
    /// Calls GET /users (no id param) using the user's own access token to get their profile.
    /// This is the correct flow after OAuth code exchange.
    /// </summary>
    private async Task<TwitchUserInfo?> GetUserFromTokenAsync(
        string accessToken,
        CancellationToken ct
    )
    {
        HttpRequestMessage request = new(HttpMethod.Get, "https://api.twitch.tv/helix/users");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("Client-Id", _options.ClientId);

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
                user.BroadcasterType
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
    }
}
