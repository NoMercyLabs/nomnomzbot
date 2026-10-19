// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// Resolves a decrypted Helix bearer for one call (twitch-helix.md §3.5) from the canonical token vault —
/// the broadcaster's user connection (Provider <c>twitch</c>) or the shared platform bot connection
/// (Provider <c>twitch_bot</c>, no broadcaster) — and exposes the connection's granted scope set for
/// pre-checks. It reads the same store the login/refresh paths write (<see cref="IIntegrationTokenVault"/> +
/// <c>IntegrationConnection</c>), never the legacy flat <c>Service</c> table. It enforces the hard invariant
/// by construction — it only ever returns a Twitch access token + a derived bucket key, never the tenant
/// <see cref="Guid"/>. On a 401 the transport calls <see cref="RefreshAsync"/>, which refreshes exactly once
/// through the auth layer (which re-vaults the new token).
///
/// The token bucket key is a salted hash of the stable token <em>identity</em> (provider + tenant), not the
/// raw token, so a refresh keeps the same bucket and the key is safe to log.
/// </summary>
public sealed class TwitchTokenResolver(
    IApplicationDbContext db,
    IIntegrationTokenVault vault,
    ITwitchAuthService authService,
    ITwitchAppTokenProvider appTokenProvider,
    IEventBus eventBus,
    IDeploymentProfileService deploymentProfile
) : ITwitchTokenResolver
{
    private const string UserProvider = AuthEnums.IntegrationProvider.Twitch;
    private const string BotProvider = AuthEnums.IntegrationProvider.Twitch + "_bot";

    // The synthetic service name for the app access token — it belongs to no tenant/user connection, so it
    // needs a stable, non-tenant identity for the bucket key and for the transport to recognise it (a 401
    // re-mints instead of refreshing, and never nags a reauth).
    internal const string AppServiceName = "twitch_app";
    private const string PlatformSubject = "_platform";

    // A personal token this close to expiry is refreshed before the call, not after a 401.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(2);

    // (broadcaster, scope) -> fingerprint of the grant the gap was last reported against. Process-wide because the
    // resolver is scoped but the snapshot builders re-check the same gap every few minutes.
    private static readonly ConcurrentDictionary<(Guid, string), string> ReportedGaps = new();

    public async Task<Result<TwitchAccessContext>> GetBotTokenAsync(CancellationToken ct = default)
    {
        // Resolution order for the bot chat identity (onboarding.md "Bot identity (two-account model)";
        // deployment-profile.md §"Everything rides this one switch" → "self-host always custom"):
        //   1. A registered custom/shared bot account — the `twitch_bot` connection (no broadcaster).
        //   2. Self-host fallback: until a bot account is registered, the bot speaks as the streamer's
        //      OWN main account — the single owner's `twitch` user connection. The streamer grant carries
        //      `user:write:chat` + `user:read:chat` (scaling-qos.md §6), so it can send/read chat as the bot.
        //      Single-tenant self-host only: on a multi-tenant profile one streamer's token is never another
        //      channel's bot, so the lookup stops at `no_token`.
        // `no_token` when neither identity exists (a fresh, un-onboarded install) or the fallback is not allowed.
        IntegrationConnection? connection =
            await ConnectionAsync(null, BotProvider, ct) ?? await OwnerUserConnectionAsync(ct);

        if (connection is null)
        {
            return Result.Failure<TwitchAccessContext>(
                "No bot token is configured.",
                TwitchErrorCodes.NoToken
            );
        }

        return await BuildContextAsync(connection, ct);
    }

    public async Task<Result<TwitchAccessContext>> GetAppTokenAsync(CancellationToken ct = default)
    {
        Result<string> token = await appTokenProvider.GetAppTokenAsync(ct);
        if (token.IsFailure)
            return token.WithValue<TwitchAccessContext>(default!);

        // Subject-agnostic: no tenant, so BroadcasterId is null and the bucket key hashes the app subject.
        return Result.Success(
            new TwitchAccessContext(
                token.Value,
                BroadcasterId: null,
                AppServiceName,
                DeriveBucketKey(AppServiceName, null)
            )
        );
    }

    public async Task<Result<TwitchAccessContext>> GetBroadcasterTokenAsync(
        Guid broadcasterId,
        bool allowBotFallback = true,
        CancellationToken ct = default
    )
    {
        IntegrationConnection? connection = await ConnectionAsync(broadcasterId, UserProvider, ct);

        if (connection is not null)
            return await BuildContextAsync(connection, ct);

        // No user token for this tenant. The bot token carries read scopes and is enough for plain Helix
        // GETs, so borrowing it keeps a partially-onboarded channel useful. But it is the WRONG identity,
        // and on any endpoint Twitch subjects to the token's own user — EventSub creates/deletes above all —
        // borrowing it fails the call AND aliases the tenant's websocket onto the bot user, burning one of
        // its 3 transports. Those callers pass allowBotFallback: false and get a loud no_token instead.
        if (!allowBotFallback)
        {
            return Result.Failure<TwitchAccessContext>(
                "No Twitch token for this broadcaster.",
                TwitchErrorCodes.NoToken
            );
        }

        return await GetBotTokenAsync(ct);
    }

    public async Task<Result<TwitchAccessContext>> GetUserTokenAsync(
        Guid userId,
        CancellationToken ct = default
    )
    {
        // The operator's OWN Twitch connection is the one whose Twitch account IS this user — matched by
        // ProviderAccountId == the user's TwitchUserId, regardless of which tenant it is filed under. This is the
        // send-as-operator identity (chat-client.md §3.1): a moderator sends in a channel they moderate as
        // themselves, on their own user:write:chat grant, never the tenant broadcaster's token.
        string? twitchUserId = await TwitchUserIdOfAsync(userId, ct);

        if (string.IsNullOrEmpty(twitchUserId))
        {
            return Result.Failure<TwitchAccessContext>(
                "No Twitch identity for this user.",
                TwitchErrorCodes.NoToken
            );
        }

        IntegrationConnection? connection = await OwnConnectionAsync(twitchUserId, ct);

        if (connection is null)
        {
            return Result.Failure<TwitchAccessContext>(
                "No Twitch connection for this user.",
                TwitchErrorCodes.NoToken
            );
        }

        return await BuildContextAsync(connection, ct);
    }

    public async Task<Result<TwitchAccessContext>> RefreshAsync(
        TwitchAccessContext context,
        CancellationToken ct = default
    )
    {
        // The app access token is not refreshable — it is re-minted. A 401 means the cached one lapsed early
        // or was revoked, so drop it and mint a fresh one for the retry.
        if (context.ServiceName == AppServiceName)
        {
            appTokenProvider.Invalidate();
            return await GetAppTokenAsync(ct);
        }

        // A personal viewer/moderator grant (no channel) refreshes by its own connection id; the
        // (null, twitch) pair names many users and must never pick the row.
        TokenResult? refreshed =
            context.BroadcasterId is null
            && context.ServiceName == UserProvider
            && context.ConnectionId is { } connectionId
                ? await authService.RefreshConnectionAsync(connectionId, ct)
                : await authService.RefreshTokenAsync(
                    context.BroadcasterId,
                    context.ServiceName,
                    ct
                );

        if (refreshed is null)
        {
            return Result.Failure<TwitchAccessContext>(
                "Token refresh failed.",
                TwitchErrorCodes.Unauthorized
            );
        }

        // The identity is unchanged, so the bucket key is stable across the refresh.
        return Result.Success(context with { AccessToken = refreshed.AccessToken });
    }

    public async Task<bool> HasScopeAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct = default
    )
    {
        IntegrationConnection? connection;
        try
        {
            connection = await ConnectionAsync(broadcasterId, UserProvider, ct);
        }
        catch when (!ct.IsCancellationRequested)
        {
            // DB schema mismatch or transient error — degrade to "scope not granted" so the
            // sub-client returns a missing_scope failure rather than a 500.
            await eventBus.PublishAsync(
                new TwitchHelixReauthRequiredEvent
                {
                    BroadcasterId = broadcasterId,
                    Provider = "twitch",
                    ServiceName = "twitch",
                    Reason = TwitchErrorCodes.NoToken,
                    MissingScope = scope,
                },
                ct
            );
            return false;
        }

        return await CheckAndReportAsync(connection, broadcasterId, scope, ct);
    }

    public async Task<bool> HasUserScopeAsync(
        Guid userId,
        string scope,
        CancellationToken ct = default
    )
    {
        string? twitchUserId = await TwitchUserIdOfAsync(userId, ct);
        if (string.IsNullOrEmpty(twitchUserId))
            return false;

        IntegrationConnection? connection = await OwnConnectionAsync(twitchUserId, ct);

        return connection is not null
            && connection.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
    }

    private Task<string?> TwitchUserIdOfAsync(Guid userId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.TwitchUserId).FirstOrDefaultAsync(ct);

    private Task<IntegrationConnection?> OwnConnectionAsync(
        string twitchUserId,
        CancellationToken ct
    ) =>
        db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.Provider == UserProvider
                && c.ProviderAccountId == twitchUserId
                && c.DeletedAt == null
            )
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> HasBotScopeAsync(
        Guid channelId,
        string scope,
        CancellationToken ct = default
    )
    {
        // The same connection GetBotTokenAsync signs with: the registered bot, else the self-host owner.
        IntegrationConnection? connection =
            await ConnectionAsync(null, BotProvider, ct) ?? await OwnerUserConnectionAsync(ct);

        return await CheckAndReportAsync(connection, channelId, scope, ct);
    }

    // The single chokepoint every sub-client's per-method scope pre-check calls — so emitting here makes the
    // proactive precheck path feed the same reactive missing-scope surface as a runtime 403, for ALL clients,
    // without touching each one. A gap already reported against this exact grant is not re-published: every
    // publish is journaled, writes a row and recomputes the inbox. A changed grant reports it again.
    private async Task<bool> CheckAndReportAsync(
        IntegrationConnection? connection,
        Guid broadcasterId,
        string scope,
        CancellationToken ct
    )
    {
        if (connection is null)
            return false;

        bool granted = connection.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
        (Guid, string) gap = (broadcasterId, scope.ToLowerInvariant());
        if (granted)
        {
            ReportedGaps.TryRemove(gap, out _);
            return true;
        }

        string fingerprint = string.Join(
            ' ',
            connection.Scopes.Select(s => s.ToLowerInvariant()).Order(StringComparer.Ordinal)
        );
        if (ReportedGaps.TryGetValue(gap, out string? seen) && seen == fingerprint)
            return false;

        ReportedGaps[gap] = fingerprint;
        await eventBus.PublishAsync(
            new TwitchHelixReauthRequiredEvent
            {
                BroadcasterId = broadcasterId,
                Provider = "twitch",
                ServiceName = "twitch",
                Reason = TwitchErrorCodes.MissingScope,
                MissingScope = scope,
            },
            ct
        );

        return false;
    }

    /// <summary>
    /// The self-host owner's own Twitch user connection used as the bot identity when no dedicated bot account
    /// is registered (onboarding.md two-account model: the main account IS the bot until a custom bot is added).
    /// Tenant isolation: a channel never borrows another streamer's token. The fallback exists only on a
    /// self-host profile (the operator IS the owner) and only while exactly one streamer <c>twitch</c>
    /// connection exists — with two or more there is no single "owner", so no row is picked.
    /// </summary>
    private async Task<IntegrationConnection?> OwnerUserConnectionAsync(CancellationToken ct)
    {
        if (deploymentProfile.Current.Mode == DeploymentMode.Saas)
            return null;

        List<IntegrationConnection> streamerConnections = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.Provider == UserProvider && c.BroadcasterId != null && c.DeletedAt == null
            )
            .Take(2)
            .ToListAsync(ct);

        return streamerConnections.Count == 1 ? streamerConnections[0] : null;
    }

    /// <summary>The active (non-deleted) connection for a <c>(tenant, provider)</c>, or null when none exists.</summary>
    private async Task<IntegrationConnection?> ConnectionAsync(
        Guid? broadcasterId,
        string provider,
        CancellationToken ct
    ) =>
        await db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.Provider == provider
                    && c.DeletedAt == null,
                ct
            );

    private async Task<Result<TwitchAccessContext>> BuildContextAsync(
        IntegrationConnection connection,
        CancellationToken ct
    )
    {
        Result<DecryptedTokenDto> token = await vault.GetAccessTokenAsync(connection.Id, ct);
        if (token.IsFailure)
        {
            return Result.Failure<TwitchAccessContext>(
                "Stored token could not be read.",
                TwitchErrorCodes.NoToken
            );
        }

        // A personal (no channel) Twitch grant is rate-limited per account, so it keys by the account id.
        bool isPersonal = connection.BroadcasterId is null && connection.Provider == UserProvider;
        string bucketKey = isPersonal
            ? DeriveBucketKey($"{UserProvider}:{connection.ProviderAccountId}", null)
            : DeriveBucketKey(connection.Provider, connection.BroadcasterId);

        string accessToken = token.Value.Value;
        if (
            isPersonal
            && token.Value.ExpiresAt is { } expiresAt
            && expiresAt <= DateTime.UtcNow.Add(ExpirySkew)
        )
        {
            // Refresh this one row by its own id before the send, so the caller never posts a dead token.
            TokenResult? refreshed = await authService.RefreshConnectionAsync(connection.Id, ct);
            if (refreshed is not null)
                accessToken = refreshed.AccessToken;
        }

        return Result.Success(
            new TwitchAccessContext(
                accessToken,
                connection.BroadcasterId,
                connection.Provider,
                bucketKey,
                connection.Id
            )
        );
    }

    /// <summary>Stable, non-secret bucket id: a short hash over the token identity (provider + tenant).</summary>
    private static string DeriveBucketKey(string provider, Guid? broadcasterId)
    {
        string identity = $"{provider}:{broadcasterId?.ToString() ?? PlatformSubject}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"helix:{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}
