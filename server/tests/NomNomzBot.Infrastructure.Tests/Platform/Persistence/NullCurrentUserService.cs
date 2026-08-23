// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Auth;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// A no-ambient-user <see cref="ICurrentUserService"/> stand-in for test DbContext harnesses that
/// wire up <c>SoftDeleteInterceptor</c> (S013d needs one to stamp <c>DeletedBy</c>) but aren't
/// themselves testing actor attribution. Every soft delete through it persists with a null
/// DeletedBy — exactly the "system/background delete with no ambient user" case the interceptor's
/// own contract already documents. Tests that DO exercise DeletedBy stamping use a real value
/// (<see cref="StubCurrentUserService.For"/>) instead.
/// </summary>
internal sealed class NullCurrentUserService : ICurrentUserService
{
    public string? UserId => null;
    public string? Username => null;
    public bool IsAuthenticated => false;
    public bool IsPlatformPrincipal => false;
    public ImpersonationContext? Impersonation => null;
}

/// <summary>
/// A fixed-identity <see cref="ICurrentUserService"/> stand-in for tests that assert
/// <c>SoftDeleteInterceptor</c> stamps <c>DeletedBy</c> with the acting user (or, during
/// impersonation, the operator).
/// </summary>
internal sealed class StubCurrentUserService : ICurrentUserService
{
    public string? UserId { get; private init; }
    public string? Username { get; private init; }
    public bool IsAuthenticated => UserId is not null;
    public bool IsPlatformPrincipal { get; private init; }
    public ImpersonationContext? Impersonation { get; private init; }

    public static StubCurrentUserService For(Guid userId, string username = "test-user") =>
        new() { UserId = userId.ToString(), Username = username };

    public static StubCurrentUserService Impersonating(
        Guid operatorUserId,
        Guid subjectUserId,
        Guid sessionId
    ) =>
        new()
        {
            UserId = subjectUserId.ToString(),
            IsPlatformPrincipal = true,
            Impersonation = new(operatorUserId, subjectUserId, sessionId),
        };
}
