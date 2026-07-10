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

    /// <summary>
    /// Attach a proven external identity (<paramref name="proof"/>) to <paramref name="userId"/> as a NON-primary
    /// linked identity (platform-identity §4). The proof comes only from a login provider's OAuth handler — the
    /// same proof→identity flow a fresh login runs, but bound to the CURRENT user instead of minting a session.
    /// Idempotent when the identity is already this user's. Fails <c>IDENTITY_ALREADY_LINKED</c> when the account
    /// belongs to a different user, and <c>PROVIDER_ALREADY_LINKED</c> when this user already has an identity for
    /// that provider (one identity per provider per user).
    /// </summary>
    Task<Result<UserIdentityDto>> LinkAsync(
        Guid userId,
        ExternalIdentityProof proof,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Unlink one of <paramref name="userId"/>'s own identities (platform-identity §4). Refuses to remove the
    /// <c>IsPrimary</c> identity (<c>PRIMARY_IDENTITY</c> — set another primary first) or the user's last
    /// remaining identity (<c>LAST_IDENTITY</c> — a user must always keep at least one), so the primary is never
    /// orphaned. <c>IDENTITY_NOT_FOUND</c> when the id is not one of the caller's identities.
    /// </summary>
    Task<Result> UnlinkAsync(
        Guid userId,
        Guid identityId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Make one of <paramref name="userId"/>'s existing identities the primary (platform-identity §4): moves the
    /// single <c>IsPrimary</c> marker to <paramref name="identityId"/>, clears it from the others, and points
    /// <c>User.Platform</c> at the new primary's provider. <c>IDENTITY_NOT_FOUND</c> when the id is not one of
    /// the caller's identities.
    /// </summary>
    Task<Result<UserIdentityDto>> SetPrimaryAsync(
        Guid userId,
        Guid identityId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Transfer the identities of an absorbed user onto a surviving user when two viewer <c>User</c> rows are
    /// merged (platform-identity §3.1a). Re-parents each absorbed identity to <paramref name="survivingUserId"/>
    /// as NON-primary, drops any whose provider the survivor already holds (one identity per provider per user),
    /// and guarantees the survivor ends with exactly one primary — never an orphaned or duplicated primary. The
    /// per-viewer domains re-key their own rows off <c>ViewerRowAbsorbedEvent</c>; this owns the identity table.
    /// </summary>
    Task<Result> MergeIdentitiesAsync(
        Guid survivingUserId,
        Guid absorbedUserId,
        CancellationToken cancellationToken = default
    );
}
