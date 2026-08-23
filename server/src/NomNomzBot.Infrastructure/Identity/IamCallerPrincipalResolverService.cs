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
using NomNomzBot.Application.Contracts.Authorization;

namespace NomNomzBot.Infrastructure.Identity;

/// <inheritdoc cref="IIamCallerPrincipalResolverService"/>
public sealed class IamCallerPrincipalResolverService(IPlatformIamService iam)
    : IIamCallerPrincipalResolverService
{
    public async Task<Result<Guid>> ResolveActingPrincipalIdAsync(
        string? userIdClaim,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userIdClaim, out Guid userId))
            return Result.Failure<Guid>(
                "The caller's identity claim could not be resolved.",
                "FORBIDDEN"
            );

        Result<IamPrincipalDto?> principal = await iam.ResolvePrincipalAsync(
            userId,
            cancellationToken
        );
        if (principal.IsFailure)
            return Result.Failure<Guid>(
                principal.ErrorMessage,
                principal.ErrorCode,
                principal.ErrorDetail
            );

        if (principal.Value is not null)
            return Result.Success(principal.Value.Id);

        // No principal row for this user. Post-D2, self-host mints a real principal for the owner during
        // bootstrap, so this is no longer the expected self-host path — deny rather than fabricate an
        // implicit-full Guid.Empty.
        return Result.Failure<Guid>("No IAM principal is registered for this caller.", "FORBIDDEN");
    }
}
