// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!lurk</c>/<c>!unlurk</c> (legacy parity, S068a) flip <see cref="User.IsLurking"/> on the caller's
/// real row and confirm it in chat — these prove the FLAG ACTUALLY FLIPS on the persisted row, not just
/// that a string came back.
/// </summary>
public sealed class LurkBuiltinsTests
{
    private const string TwitchId = "42";
    private const string Login = "stoney_eagle";

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = TwitchId,
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = Login,
        };

    private static IUserService FakeUsers() =>
        Substitute
            .For<IUserService>()
            .Also(u =>
                u.GetOrCreateAsync(
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<CancellationToken>()
                    )
                    .Returns(
                        Result.Success(
                            new UserDto(
                                Guid.CreateVersion7().ToString(),
                                Login,
                                "Stoney_Eagle",
                                ProfileImageUrl: null,
                                Email: null,
                                CreatedAt: DateTime.UtcNow,
                                LastLoginAt: DateTime.UtcNow
                            )
                        )
                    )
            );

    [Fact]
    public async Task Lurk_sets_IsLurking_true_on_the_real_row_and_confirms_it()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = TwitchId,
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
                IsLurking = false,
            }
        );
        await db.SaveChangesAsync();

        LurkBuiltin builtin = new(FakeUsers(), db);

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("lurking");
        db.Users.Single(u => u.TwitchUserId == TwitchId).IsLurking.Should().BeTrue();
    }

    [Fact]
    public async Task Unlurk_clears_IsLurking_on_the_real_row_and_confirms_it()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = TwitchId,
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
                IsLurking = true,
            }
        );
        await db.SaveChangesAsync();

        UnlurkBuiltin builtin = new(FakeUsers(), db);

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("no longer lurking");
        db.Users.Single(u => u.TwitchUserId == TwitchId).IsLurking.Should().BeFalse();
    }
}

file static class SubstituteExtensions
{
    public static T Also<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
