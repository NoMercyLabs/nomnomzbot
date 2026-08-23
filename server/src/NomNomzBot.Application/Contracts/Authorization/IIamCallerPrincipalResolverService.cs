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

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// Resolves the authenticated caller's Plane-C ACTING principal id (fix D2 item 4). Single funnel for what
/// <c>PlatformIamController</c> and <c>PlatformAdminController</c> used to duplicate as an identical private
/// helper each — the duplicate silently folded every failure (a user id claim that doesn't parse, or no
/// <c>IamPrincipal</c> row for that user) into <see cref="Guid.Empty"/>, which the service's self-host
/// short-circuit then treated as an implicit ALLOW. A resolve failure now DENIES; it is never fabricated into
/// an ambient full-access id.
/// </summary>
public interface IIamCallerPrincipalResolverService
{
    /// <summary>
    /// Resolves <paramref name="userIdClaim"/> (the caller's raw <c>ICurrentUserService.UserId</c> claim) to
    /// their IAM principal id. Fails when the claim does not parse as a <see cref="Guid"/> or no principal row
    /// exists for that user — callers MUST deny on failure, never substitute <see cref="Guid.Empty"/>.
    /// </summary>
    Task<Result<Guid>> ResolveActingPrincipalIdAsync(
        string? userIdClaim,
        CancellationToken cancellationToken = default
    );
}
