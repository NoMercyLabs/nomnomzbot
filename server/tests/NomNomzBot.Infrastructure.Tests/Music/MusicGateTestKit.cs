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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Builds a <see cref="MusicModerationGate"/> whose IAM answer the test controls.</summary>
internal static class MusicGateTestKit
{
    /// <summary>The internal user id every chatter resolves to.</summary>
    public static readonly Guid ChatterUserId = Guid.Parse("0192a000-0000-7000-8000-00000000c0de");

    /// <summary>
    /// A gate whose resolver reports <paramref name="holdsGrant"/> for the
    /// <see cref="MusicModerationGate.ActionKey"/> action. The returned resolver lets a test assert which
    /// action key and user the gate asked about.
    /// </summary>
    public static MusicModerationGate Gate(bool holdsGrant, out IRoleResolver roles)
    {
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Result.Success(UserWithId(ChatterUserId)));

        roles = Substitute.For<IRoleResolver>();
        roles
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(holdsGrant));
        return new MusicModerationGate(users, roles);
    }

    public static MusicModerationGate Gate(bool holdsGrant) => Gate(holdsGrant, out _);

    private static UserDto UserWithId(Guid id) =>
        new(
            id.ToString(),
            "chatter",
            "Chatter",
            null,
            null,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch
        );
}
