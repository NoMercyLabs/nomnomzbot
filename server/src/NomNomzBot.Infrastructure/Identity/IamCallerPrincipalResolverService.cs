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
