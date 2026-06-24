// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
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
    IEventBus eventBus
) : ITwitchTokenResolver
{
    private const string UserProvider = AuthEnums.IntegrationProvider.Twitch;
    private const string BotProvider = AuthEnums.IntegrationProvider.Twitch + "_bot";
    private const string PlatformSubject = "_platform";

    public async Task<Result<TwitchAccessContext>> GetBotTokenAsync(CancellationToken ct = default)
    {
        // Resolution order for the bot chat identity (onboarding.md "Bot identity (two-account model)";
        // deployment-profile.md §"Everything rides this one switch" → "self-host always custom"):
        //   1. A registered custom/shared bot account — the `twitch_bot` connection (no broadcaster).
        //   2. Self-host fallback: until a bot account is registered, the bot speaks as the streamer's
        //      OWN main account — the single owner's `twitch` user connection. The streamer grant carries
        //      `user:write:chat` + `user:read:chat` (scaling-qos.md §6), so it can send/read chat as the bot.
        // `no_token` only when neither identity exists (a fresh, un-onboarded install).
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

    public async Task<Result<TwitchAccessContext>> GetBroadcasterTokenAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        IntegrationConnection? connection = await ConnectionAsync(broadcasterId, UserProvider, ct);

        // No user token for this tenant — fall back to the bot token (read scopes only).
        if (connection is null)
            return await GetBotTokenAsync(ct);

        return await BuildContextAsync(connection, ct);
    }

    public async Task<Result<TwitchAccessContext>> RefreshAsync(
        TwitchAccessContext context,
        CancellationToken ct = default
    )
    {
        TokenResult? refreshed = await authService.RefreshTokenAsync(
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
        IntegrationConnection? connection = await ConnectionAsync(broadcasterId, UserProvider, ct);
        bool granted =
            connection is not null
            && connection.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);

        // The single chokepoint every sub-client's per-method scope pre-check calls — so emitting here makes the
        // proactive precheck path feed the same reactive missing-scope surface as a runtime 403, for ALL clients,
        // without touching each one. The handler is idempotent, so re-emitting on every failed call is harmless.
        if (!granted && connection is not null)
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

        return granted;
    }

    /// <summary>
    /// The self-host owner's own Twitch user connection used as the bot identity when no dedicated bot account
    /// is registered (onboarding.md two-account model: the main account IS the bot until a custom bot is added).
    /// Self-host is single-tenant (deployment-profile.md), so there is exactly one streamer <c>twitch</c>
    /// connection; ordered by creation so the result is deterministic if more than one ever exists.
    /// </summary>
    private async Task<IntegrationConnection?> OwnerUserConnectionAsync(CancellationToken ct) =>
        await db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.Provider == UserProvider && c.BroadcasterId != null && c.DeletedAt == null
            )
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

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

        string bucketKey = DeriveBucketKey(connection.Provider, connection.BroadcasterId);
        return Result.Success(
            new TwitchAccessContext(
                token.Value.Value,
                connection.BroadcasterId,
                connection.Provider,
                bucketKey
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
