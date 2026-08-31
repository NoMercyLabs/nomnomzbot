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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!accountage</c> (legacy parity, S068a) reports how long the caller's Twitch account has existed.
/// These prove the DURATION IS ACTUALLY COMPUTED from a real <c>created_at</c> — once already hydrated
/// on the row, and once resolved live via Helix when the row was never hydrated (and then persisted).
/// </summary>
public sealed class AccountAgeBuiltinTests
{
    private const string TwitchId = "42";
    private const string Login = "stoney_eagle";
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = TwitchId,
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = Login,
        };

    private static IUserService FakeUsers()
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
            );
        return users;
    }

    [Fact]
    public async Task Reports_the_real_duration_from_an_already_hydrated_row()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = TwitchId,
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
                // Exactly 2 years before "now" — 0 months remainder.
                AccountCreatedAt = Now.UtcDateTime.AddYears(-2),
            }
        );
        await db.SaveChangesAsync();

        AccountAgeBuiltin builtin = new(
            Substitute.For<ITwitchUsersApi>(),
            FakeUsers(),
            db,
            new FakeTimeProvider(Now)
        );

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("2 years");
    }

    [Fact]
    public async Task An_unhydrated_row_resolves_created_at_from_Helix_and_persists_it()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = TwitchId,
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
                AccountCreatedAt = null,
            }
        );
        await db.SaveChangesAsync();

        DateTimeOffset createdAt = Now.AddYears(-1).AddMonths(-3);
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    new TwitchUser(
                        Id: TwitchId,
                        Login: Login,
                        DisplayName: "Stoney_Eagle",
                        Type: "",
                        BroadcasterType: "affiliate",
                        Description: "",
                        ProfileImageUrl: "https://cdn/avatar.png",
                        OfflineImageUrl: "",
                        ViewCount: 0,
                        CreatedAt: createdAt
                    ),
                ])
            );

        AccountAgeBuiltin builtin = new(twitch, FakeUsers(), db, new FakeTimeProvider(Now));

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("1 year");
        result.Value.Should().Contain("3 months");
        // The point of the fallback: the row now carries the resolved date so next time is free.
        db.Users.Single(u => u.TwitchUserId == TwitchId)
            .AccountCreatedAt.Should()
            .Be(createdAt.UtcDateTime);
    }

    [Fact]
    public async Task A_Helix_failure_on_an_unhydrated_row_is_reported_as_a_platform_failure()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = TwitchId,
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
                AccountCreatedAt = null,
            }
        );
        await db.SaveChangesAsync();

        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<TwitchUser>>(
                    "The operation didn't complete within the allowed timeout.",
                    "TIMEOUT"
                )
            );

        AccountAgeBuiltin builtin = new(twitch, FakeUsers(), db, new FakeTimeProvider(Now));

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue("the command always answers rather than going silent");
        result.Value.Should().Contain("try again");
    }
}
