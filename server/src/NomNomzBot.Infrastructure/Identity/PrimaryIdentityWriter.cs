// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Keeps a user's primary external identity in sync as they are seen (platform-identity §3.1/§8.1). Every path
/// that mints or refreshes a <see cref="User"/> — device/redirect login, the chat get-or-create, the resolver —
/// calls this so the identity table stays live in real time rather than only at boot backfill. Writes are left
/// TRACKED for the caller's own <c>SaveChanges</c>/transaction; profile fields refresh only when they actually
/// changed, so the hot chat path stays quiet on a no-op re-see.
/// </summary>
internal static class PrimaryIdentityWriter
{
    public static async Task EnsureAsync(
        IApplicationDbContext db,
        TimeProvider clock,
        Guid userId,
        string provider,
        string providerUserId,
        string username,
        string? displayName,
        string? avatarUrl,
        Guid? connectionId = null,
        CancellationToken cancellationToken = default
    )
    {
        UserIdentity? identity = await db.UserIdentities.FirstOrDefaultAsync(
            i => i.Provider == provider && i.ProviderUserId == providerUserId,
            cancellationToken
        );

        if (identity is null)
        {
            db.UserIdentities.Add(
                new()
                {
                    UserId = userId,
                    Provider = provider,
                    ProviderUserId = providerUserId,
                    ProviderUsername = username,
                    ProviderDisplayName = displayName,
                    ProviderAvatarUrl = avatarUrl,
                    ConnectionId = connectionId,
                    IsPrimary = true,
                    LinkedAt = clock.GetUtcNow().UtcDateTime,
                }
            );
            return;
        }

        // Refresh the denormalised profile only when a value actually changed (no-op re-see writes nothing).
        if (identity.ProviderUsername != username)
            identity.ProviderUsername = username;
        if (displayName is not null && identity.ProviderDisplayName != displayName)
            identity.ProviderDisplayName = displayName;
        if (avatarUrl is not null && identity.ProviderAvatarUrl != avatarUrl)
            identity.ProviderAvatarUrl = avatarUrl;
        if (connectionId is not null && identity.ConnectionId != connectionId)
            identity.ConnectionId = connectionId;
    }
}
