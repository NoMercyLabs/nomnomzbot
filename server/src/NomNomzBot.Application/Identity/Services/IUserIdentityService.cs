// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// The platform-agnostic identity resolver (platform-identity §3.1). Every ingest path (chat, EventSub,
/// roster/standing sync, journal attribution) resolves an external <c>(provider, providerUserId)</c> to the
/// internal <see cref="Guid"/> user through <see cref="ResolveUserAsync"/> — replacing direct
/// <c>TwitchUserId</c> lookups. Linking, unlinking and primary-selection are added alongside the identity CRUD
/// surface; this contract grows additively.
/// </summary>
public interface IUserIdentityService
{
    /// <summary>All identities of a user, primary first.</summary>
    Task<Result<IReadOnlyList<UserIdentityDto>>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Resolve an external <paramref name="provider"/>/<paramref name="providerUserId"/> to the internal user
    /// id. With <paramref name="getOrCreate"/> the viewer-identity rule applies (a chatter IS a User row):
    /// the <c>User</c> + <c>UserIdentity</c> pair is created when unseen. Failure <c>IDENTITY_NOT_FOUND</c>
    /// when the identity is absent and <paramref name="getOrCreate"/> is false.
    /// </summary>
    Task<Result<Guid>> ResolveUserAsync(
        string provider,
        string providerUserId,
        bool getOrCreate,
        CancellationToken cancellationToken = default
    );
}
