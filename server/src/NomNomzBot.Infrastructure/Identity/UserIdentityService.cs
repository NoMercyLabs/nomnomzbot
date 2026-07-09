// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Platform-agnostic identity resolver (platform-identity §3.1). Resolves an external
/// <c>(provider, providerUserId)</c> to the internal user id, creating the <see cref="User"/> +
/// <see cref="UserIdentity"/> pair on demand for the viewer-identity rule. Twitch is the only provider that
/// can mint a user while <c>User.TwitchUserId</c> is still non-nullable; other providers become creatable when
/// the nullable-projection migration lands.
/// </summary>
public sealed class UserIdentityService : IUserIdentityService
{
    private readonly IApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;

    public UserIdentityService(
        IApplicationDbContext db,
        IServiceScopeFactory scopeFactory,
        TimeProvider clock
    )
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<UserIdentityDto>>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    )
    {
        List<UserIdentityDto> identities = await _db
            .UserIdentities.Where(i => i.UserId == userId)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.LinkedAt)
            .Select(i => new UserIdentityDto(
                i.Provider,
                i.ProviderUserId,
                i.ProviderUsername,
                i.ProviderDisplayName,
                i.ProviderAvatarUrl,
                i.IsPrimary,
                i.LinkedAt,
                i.LastLoginAt
            ))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<UserIdentityDto>>(identities);
    }

    public async Task<Result<Guid>> ResolveUserAsync(
        string provider,
        string providerUserId,
        bool getOrCreate,
        CancellationToken cancellationToken = default
    )
    {
        string normalizedProvider = provider.ToLowerInvariant();

        Guid existing = await _db
            .UserIdentities.Where(i =>
                i.Provider == normalizedProvider && i.ProviderUserId == providerUserId
            )
            .Select(i => i.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != Guid.Empty)
            return Result.Success(existing);

        if (!getOrCreate)
            return Result.Failure<Guid>(
                $"No identity for {normalizedProvider}:{providerUserId}.",
                "IDENTITY_NOT_FOUND"
            );

        // Create in a dedicated scope so this never races the caller's scoped DbContext (mirrors
        // UserService.GetOrCreateAsync, which can be called inside seed loops alongside other queries).
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        UserIdentity? identity = await db.UserIdentities.FirstOrDefaultAsync(
            i => i.Provider == normalizedProvider && i.ProviderUserId == providerUserId,
            cancellationToken
        );
        if (identity is not null)
            return Result.Success(identity.UserId);

        Result<Guid> ownerResult = await ResolveOwnerUserIdAsync(
            db,
            normalizedProvider,
            providerUserId,
            cancellationToken
        );
        if (ownerResult.IsFailure)
            return ownerResult;

        db.UserIdentities.Add(
            new UserIdentity
            {
                UserId = ownerResult.Value,
                Provider = normalizedProvider,
                ProviderUserId = providerUserId,
                ProviderUsername = providerUserId, // placeholder until enriched with the real username
                IsPrimary = true,
                LinkedAt = _clock.GetUtcNow().UtcDateTime,
            }
        );
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ownerResult.Value);
    }

    /// <summary>
    /// Finds the internal user to attach a fresh identity to. For Twitch a pre-identity <see cref="User"/>
    /// keyed by <c>TwitchUserId</c> may already exist (created before the identity table, or via the chat
    /// get-or-create) — reuse it. Otherwise mint a new user.
    /// </summary>
    private async Task<Result<Guid>> ResolveOwnerUserIdAsync(
        IApplicationDbContext db,
        string normalizedProvider,
        string providerUserId,
        CancellationToken cancellationToken
    )
    {
        if (normalizedProvider == AuthEnums.Platform.Twitch)
        {
            User? legacy = await db.Users.FirstOrDefaultAsync(
                u => u.TwitchUserId == providerUserId,
                cancellationToken
            );
            if (legacy is not null)
                return Result.Success(legacy.Id);

            User created = new()
            {
                TwitchUserId = providerUserId,
                Platform = AuthEnums.Platform.Twitch,
                Username = providerUserId,
                UsernameNormalized = providerUserId.ToLowerInvariant(),
                DisplayName = providerUserId,
                Enabled = true,
            };
            db.Users.Add(created);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(created.Id);
        }

        // Minting a user for a non-Twitch provider needs the nullable TwitchUserId projection (next sub-slice).
        return Result.Failure<Guid>(
            $"Creating a user for provider '{normalizedProvider}' is not yet supported.",
            "PROVIDER_NOT_CREATABLE"
        );
    }
}
