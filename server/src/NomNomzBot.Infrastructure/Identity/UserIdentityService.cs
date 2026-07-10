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
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

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
    private readonly IEventBus _events;

    public UserIdentityService(
        IApplicationDbContext db,
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        IEventBus events
    )
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _events = events;
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

        // ProviderUsername is a placeholder here (this path only has the external id); the login / chat
        // get-or-create paths call the same writer with the real profile to enrich it.
        await PrimaryIdentityWriter.EnsureAsync(
            db,
            _clock,
            ownerResult.Value,
            normalizedProvider,
            providerUserId,
            username: providerUserId,
            displayName: null,
            avatarUrl: null,
            cancellationToken: cancellationToken
        );
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ownerResult.Value);
    }

    public async Task<Result<UserIdentityDto>> LinkAsync(
        Guid userId,
        ExternalIdentityProof proof,
        CancellationToken cancellationToken = default
    )
    {
        string normalizedProvider = proof.Provider.ToLowerInvariant();

        UserIdentity? existing = await _db.UserIdentities.FirstOrDefaultAsync(
            i => i.Provider == normalizedProvider && i.ProviderUserId == proof.ProviderUserId,
            cancellationToken
        );
        if (existing is not null)
        {
            if (existing.UserId != userId)
                return Result.Failure<UserIdentityDto>(
                    $"This {normalizedProvider} account is already linked to another user.",
                    "IDENTITY_ALREADY_LINKED"
                );

            // Idempotent re-link of the caller's own identity: refresh the denormalised profile, keep its role.
            await PrimaryIdentityWriter.EnsureAsync(
                _db,
                _clock,
                userId,
                normalizedProvider,
                proof.ProviderUserId,
                proof.Username,
                proof.DisplayName,
                proof.AvatarUrl,
                proof.ConnectionId,
                cancellationToken
            );
            await _db.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(existing));
        }

        // One identity per provider per user (schema A.6, IX_UserIdentity_UserId_Provider).
        bool providerTaken = await _db.UserIdentities.AnyAsync(
            i => i.UserId == userId && i.Provider == normalizedProvider,
            cancellationToken
        );
        if (providerTaken)
            return Result.Failure<UserIdentityDto>(
                $"You already have a {normalizedProvider} identity linked. Unlink it first.",
                "PROVIDER_ALREADY_LINKED"
            );

        bool userExists = await _db.Users.AnyAsync(u => u.Id == userId, cancellationToken);
        if (!userExists)
            return Result.Failure<UserIdentityDto>("User not found.", "NOT_FOUND");

        // A linked identity is never primary on creation — the user already has a primary from their login.
        UserIdentity identity = new()
        {
            UserId = userId,
            Provider = normalizedProvider,
            ProviderUserId = proof.ProviderUserId,
            ProviderUsername = proof.Username,
            ProviderDisplayName = proof.DisplayName,
            ProviderAvatarUrl = proof.AvatarUrl,
            ConnectionId = proof.ConnectionId,
            IsPrimary = false,
            LinkedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.UserIdentities.Add(identity);
        await _db.SaveChangesAsync(cancellationToken);

        await _events.PublishAsync(
            new UserIdentityLinkedEvent
            {
                UserId = userId,
                Provider = normalizedProvider,
                ProviderUserId = proof.ProviderUserId,
                ProviderUsername = proof.Username,
            },
            cancellationToken
        );

        return Result.Success(ToDto(identity));
    }

    public async Task<Result> UnlinkAsync(
        Guid userId,
        Guid identityId,
        CancellationToken cancellationToken = default
    )
    {
        UserIdentity? identity = await _db.UserIdentities.FirstOrDefaultAsync(
            i => i.Id == identityId && i.UserId == userId,
            cancellationToken
        );
        if (identity is null)
            return Result.Failure("Identity not found.", "IDENTITY_NOT_FOUND");

        // The primary can't be orphaned — the caller re-designates a primary before unlinking this one.
        if (identity.IsPrimary)
            return Result.Failure(
                "Can't unlink your primary identity — set another as primary first.",
                "PRIMARY_IDENTITY"
            );

        // A user must always keep at least one identity to sign in with.
        int remaining = await _db.UserIdentities.CountAsync(
            i => i.UserId == userId,
            cancellationToken
        );
        if (remaining <= 1)
            return Result.Failure(
                "Can't unlink your last identity — you must keep at least one.",
                "LAST_IDENTITY"
            );

        // Remove() is converted to a soft delete (DeletedAt stamp) by SoftDeleteInterceptor; the partial unique
        // indexes (WHERE DeletedAt IS NULL) free the slot so the account can be linked again later.
        _db.UserIdentities.Remove(identity);
        await _db.SaveChangesAsync(cancellationToken);

        await _events.PublishAsync(
            new UserIdentityUnlinkedEvent
            {
                UserId = userId,
                Provider = identity.Provider,
                ProviderUserId = identity.ProviderUserId,
                Reason = "user_unlinked",
            },
            cancellationToken
        );

        return Result.Success();
    }

    public async Task<Result<UserIdentityDto>> SetPrimaryAsync(
        Guid userId,
        Guid identityId,
        CancellationToken cancellationToken = default
    )
    {
        List<UserIdentity> identities = await _db
            .UserIdentities.Where(i => i.UserId == userId)
            .ToListAsync(cancellationToken);

        UserIdentity? target = identities.FirstOrDefault(i => i.Id == identityId);
        if (target is null)
            return Result.Failure<UserIdentityDto>("Identity not found.", "IDENTITY_NOT_FOUND");

        if (target.IsPrimary)
            return Result.Success(ToDto(target));

        // Exactly one identity is primary — move the marker onto the target and clear it from every other.
        foreach (UserIdentity identity in identities)
            identity.IsPrimary = identity.Id == identityId;

        // The primary seeds User.Platform (schema A.6) — keep it pointed at the new primary's provider.
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is not null)
            user.Platform = target.Provider;

        await _db.SaveChangesAsync(cancellationToken);

        await _events.PublishAsync(
            new PrimaryIdentityChangedEvent { UserId = userId, Provider = target.Provider },
            cancellationToken
        );

        return Result.Success(ToDto(target));
    }

    public async Task<Result> MergeIdentitiesAsync(
        Guid survivingUserId,
        Guid absorbedUserId,
        CancellationToken cancellationToken = default
    )
    {
        if (survivingUserId == absorbedUserId)
            return Result.Failure("Cannot merge a user into itself.", "VALIDATION_FAILED");

        List<UserIdentity> survivingIdentities = await _db
            .UserIdentities.Where(i => i.UserId == survivingUserId)
            .ToListAsync(cancellationToken);
        List<UserIdentity> absorbedIdentities = await _db
            .UserIdentities.Where(i => i.UserId == absorbedUserId)
            .ToListAsync(cancellationToken);

        if (absorbedIdentities.Count == 0)
            return Result.Success();

        HashSet<string> heldProviders = survivingIdentities
            .Select(i => i.Provider)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool survivorHasPrimary = survivingIdentities.Any(i => i.IsPrimary);
        Guid? absorbedPrimaryId = absorbedIdentities.FirstOrDefault(i => i.IsPrimary)?.Id;

        List<UserIdentity> reparented = [];
        foreach (UserIdentity identity in absorbedIdentities)
        {
            if (heldProviders.Contains(identity.Provider))
            {
                // The survivor already owns this provider — drop the duplicate (one identity per provider per user).
                _db.UserIdentities.Remove(identity);
                continue;
            }

            identity.UserId = survivingUserId;
            identity.IsPrimary = false; // the survivor keeps its own primary; never mint a second.
            heldProviders.Add(identity.Provider);
            reparented.Add(identity);
        }

        // Guarantee exactly one primary: if the survivor had none and we moved at least one identity over, promote
        // one — preferring what was the absorbed user's primary — so the primary is never left orphaned.
        if (!survivorHasPrimary && reparented.Count > 0)
        {
            UserIdentity promote =
                reparented.FirstOrDefault(i => i.Id == absorbedPrimaryId) ?? reparented[0];
            promote.IsPrimary = true;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static UserIdentityDto ToDto(UserIdentity i) =>
        new(
            i.Provider,
            i.ProviderUserId,
            i.ProviderUsername,
            i.ProviderDisplayName,
            i.ProviderAvatarUrl,
            i.IsPrimary,
            i.LinkedAt,
            i.LastLoginAt
        );

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
        // Twitch has pre-identity users keyed by TwitchUserId (created before the identity table / via chat
        // get-or-create) — reuse one if present rather than minting a duplicate.
        if (normalizedProvider == AuthEnums.Platform.Twitch)
        {
            User? legacy = await db.Users.FirstOrDefaultAsync(
                u => u.TwitchUserId == providerUserId,
                cancellationToken
            );
            if (legacy is not null)
                return Result.Success(legacy.Id);
        }

        // Mint the user. TwitchUserId is the hot-path projection only for a Twitch identity; a YouTube/Kick/
        // Twitter user has none (nullable projection, platform-identity §1). Username is a placeholder here —
        // the login/chat paths enrich it with the real profile.
        bool isTwitch = normalizedProvider == AuthEnums.Platform.Twitch;
        User created = new()
        {
            TwitchUserId = isTwitch ? providerUserId : null,
            Platform = normalizedProvider,
            Username = providerUserId,
            UsernameNormalized = providerUserId.ToLowerInvariant(),
            DisplayName = providerUserId,
            Enabled = true,
        };
        db.Users.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Id);
    }
}
