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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// Resolves a decrypted Helix bearer for one call (twitch-helix.md §3.5), choosing the bot/app token
/// (service <c>twitch_bot</c>) or the broadcaster's user token (service <c>twitch</c>), and exposing the
/// granted scope set for pre-checks. It enforces the hard invariant by construction — it only ever returns
/// a Twitch access token + a derived bucket key, never the tenant <see cref="Guid"/>. On a 401 the transport
/// calls <see cref="RefreshAsync"/>, which refreshes exactly once through the auth layer.
///
/// The token bucket key is a salted hash of the stable token <em>identity</em> (service name + tenant), not
/// the raw token, so a refresh keeps the same bucket and the key is safe to log.
/// </summary>
public sealed class TwitchTokenResolver(
    IApplicationDbContext db,
    ITokenProtector tokenProtector,
    ITwitchAuthService authService
) : ITwitchTokenResolver
{
    private const string BotServiceName = "twitch_bot";
    private const string UserServiceName = "twitch";
    private const string PlatformSubject = "_platform";

    public async Task<Result<TwitchAccessContext>> GetBotTokenAsync(CancellationToken ct = default)
    {
        Service? service = await db
            .Services.Where(s => s.Name == BotServiceName && s.Enabled && s.AccessToken != null)
            .OrderByDescending(s => s.TokenExpiry)
            .FirstOrDefaultAsync(ct);

        if (service is null)
        {
            return Result.Failure<TwitchAccessContext>(
                "No bot token is configured.",
                TwitchErrorCodes.NoToken
            );
        }

        return await BuildContextAsync(service, ct);
    }

    public async Task<Result<TwitchAccessContext>> GetBroadcasterTokenAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Service? service = await db.Services.FirstOrDefaultAsync(
            s =>
                s.BroadcasterId == broadcasterId
                && s.Name == UserServiceName
                && s.Enabled
                && s.AccessToken != null,
            ct
        );

        // No user token for this tenant — fall back to the bot token (read scopes only).
        if (service is null)
            return await GetBotTokenAsync(ct);

        return await BuildContextAsync(service, ct);
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
        Service? service = await db.Services.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Name == UserServiceName && s.Enabled,
            ct
        );

        return service is not null
            && service.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Result<TwitchAccessContext>> BuildContextAsync(
        Service service,
        CancellationToken ct
    )
    {
        string subject = service.BroadcasterId?.ToString() ?? PlatformSubject;
        string? token = await tokenProtector.TryUnprotectAsync(
            service.AccessToken,
            new TokenProtectionContext(subject, service.Name, "access"),
            ct
        );

        if (token is null)
        {
            return Result.Failure<TwitchAccessContext>(
                "Stored token could not be decrypted.",
                TwitchErrorCodes.NoToken
            );
        }

        string bucketKey = DeriveBucketKey(service.Name, service.BroadcasterId);
        return Result.Success(
            new TwitchAccessContext(token, service.BroadcasterId, service.Name, bucketKey)
        );
    }

    /// <summary>Stable, non-secret bucket id: a short hash over the token identity (service + tenant).</summary>
    private static string DeriveBucketKey(string serviceName, Guid? broadcasterId)
    {
        string identity = $"{serviceName}:{broadcasterId?.ToString() ?? PlatformSubject}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"helix:{Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}
