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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The generic, platform-agnostic login (platform-identity §3.3). Every non-Twitch provider's OAuth handler
/// funnels its <see cref="ExternalIdentityProof"/> here: resolve/create the user + primary identity, link the
/// vaulted login connection, enrich the profile, then issue a TENANT-LESS session (no channel yet). Twitch
/// keeps its own <c>AuthService</c> streamer path (which also onboards the owner's channel).
/// </summary>
public sealed class ExternalLoginService : IExternalLoginService
{
    private readonly IApplicationDbContext _db;
    private readonly IUserIdentityService _identities;
    private readonly ISessionService _sessions;
    private readonly TimeProvider _clock;

    public ExternalLoginService(
        IApplicationDbContext db,
        IUserIdentityService identities,
        ISessionService sessions,
        TimeProvider clock
    )
    {
        _db = db;
        _identities = identities;
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<Result<AuthResultDto>> LoginAsync(
        ExternalIdentityProof proof,
        AuthContextDto context,
        CancellationToken cancellationToken = default
    )
    {
        // 1. Resolve/create the internal user + its primary identity for this external account.
        Result<Guid> resolved = await _identities.ResolveUserAsync(
            proof.Provider,
            proof.ProviderUserId,
            getOrCreate: true,
            cancellationToken
        );
        if (resolved.IsFailure)
            return resolved.WithValue<AuthResultDto>(null!);
        Guid userId = resolved.Value;

        // 2. Enrich the identity (real profile + the vaulted login connection).
        await PrimaryIdentityWriter.EnsureAsync(
            _db,
            _clock,
            userId,
            proof.Provider,
            proof.ProviderUserId,
            proof.Username,
            proof.DisplayName,
            proof.AvatarUrl,
            proof.ConnectionId,
            cancellationToken
        );

        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result.Failure<AuthResultDto>("User not found after resolve.", "NOT_FOUND");

        // Refresh the user's own display fields from the proof (the resolver create leaves placeholders).
        if (user.Username != proof.Username)
        {
            user.Username = proof.Username;
            user.UsernameNormalized = proof.Username.ToLowerInvariant();
        }
        if (proof.DisplayName is not null && user.DisplayName != proof.DisplayName)
            user.DisplayName = proof.DisplayName;
        if (proof.AvatarUrl is not null && user.ProfileImageUrl != proof.AvatarUrl)
            user.ProfileImageUrl = proof.AvatarUrl;
        user.LastSeenAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);

        // 3. Tenant-less session: a brand-new non-Twitch user has no channel — they land on the picker.
        Result<SessionTokensDto> session = await _sessions.CreateSessionAsync(
            userId,
            broadcasterId: null,
            context,
            cancellationToken
        );
        if (session.IsFailure)
            return session.WithValue<AuthResultDto>(null!);

        UserDto userDto = new(
            user.Id.ToString(),
            user.Username,
            user.DisplayName,
            user.ProfileImageUrl,
            null,
            user.CreatedAt,
            user.UpdatedAt
        );
        return Result.Success(
            new AuthResultDto(
                session.Value.AccessToken,
                session.Value.RawRefreshToken,
                session.Value.AccessExpiresAt,
                userDto
            )
        );
    }
}
