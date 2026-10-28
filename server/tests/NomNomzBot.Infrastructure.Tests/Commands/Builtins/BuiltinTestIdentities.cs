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
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// An <see cref="IUserIdentityService"/> for builtin tests whose in-memory context has no identity table:
/// a Twitch id resolves to the <c>Users</c> row keyed by <c>TwitchUserId</c> (what the real service does for
/// a pre-identity Twitch user); any other provider, or an unknown id, fails with IDENTITY_NOT_FOUND.
/// </summary>
internal static class BuiltinTestIdentities
{
    public static IUserIdentityService ResolvingTwitchUsersOf(CommandsTestDbContext db)
    {
        IUserIdentityService identities = Substitute.For<IUserIdentityService>();
        identities
            .ResolveUserAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string provider = call.ArgAt<string>(0);
                string providerUserId = call.ArgAt<string>(1);
                Guid id =
                    provider == AuthEnums.Platform.Twitch
                        ? db
                            .Users.Where(u => u.TwitchUserId == providerUserId)
                            .Select(u => u.Id)
                            .FirstOrDefault()
                        : Guid.Empty;
                return Task.FromResult(
                    id == Guid.Empty
                        ? Result.Failure<Guid>("No identity.", "IDENTITY_NOT_FOUND")
                        : Result.Success(id)
                );
            });
        return identities;
    }
}
