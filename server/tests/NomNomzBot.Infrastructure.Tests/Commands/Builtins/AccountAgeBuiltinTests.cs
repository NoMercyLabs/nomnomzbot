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

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!accountage</c> (legacy parity, S068a) reports how long the caller's Twitch account has existed.
/// These prove the DURATION IS ACTUALLY COMPUTED from a real <c>created_at</c> — once already hydrated
/// on the row, and once resolved live via Helix when the row was never hydrated (and then persisted) — and
/// that the success reply renders in the channel's personality tone (S069a).
/// </summary>
public sealed class AccountAgeBuiltinTests
{
    private const string TwitchId = "42";
    private const string Login = "stoney_eagle";
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    private static BuiltinCommandContext Context(
        string personality = PersonalityTone.Informative,
        string? platform = null,
        string args = ""
    ) =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = TwitchId,
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = Login,
            Personality = personality,
            TriggeringPlatform = platform,
            Args = args,
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

    private static TwitchUser TwitchUserOf(
        string id,
        string login,
        string display,
        DateTimeOffset at
    ) =>
        new(
            Id: id,
            Login: login,
            DisplayName: display,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: at
        );

    private static AccountAgeBuiltin BuiltinFor(
        CommandsTestDbContext db,
        ITwitchUsersApi twitch,
        IUserService users
    ) =>
        new(
            twitch,
            users,
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestMentionTargets.LoginsOnly(),
            TestBuiltinComposer.Create(),
            new FakeTimeProvider(Now)
        );

    [Fact]
    public async Task A_target_with_no_local_row_is_read_from_Helix_by_login_and_no_row_is_written()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    TwitchUserOf("777", "ifillz", "iFiLLz", Now.AddYears(-2)),
                ])
            );
        IUserService users = FakeUsers();

        Result<string> result = await BuiltinFor(db, twitch, users)
            .ExecuteAsync(Context(args: "@iFiLLz"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("iFiLLz's account was created on August 31, 2024 (2 years ago).");
        await twitch
            .Received(1)
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "ifillz"),
                Arg.Any<CancellationToken>()
            );
        await twitch
            .DidNotReceive()
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        db.Users.Count().Should().Be(0, "a target who never chatted here gets no row");
        await users
            .DidNotReceive()
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_target_with_a_hydrated_local_row_is_answered_from_the_row_without_Helix()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = "777",
                Username = "ifillz",
                UsernameNormalized = "ifillz",
                DisplayName = "iFiLLz",
                AccountCreatedAt = Now.UtcDateTime.AddYears(-5),
            }
        );
        await db.SaveChangesAsync();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();

        Result<string> result = await BuiltinFor(db, twitch, FakeUsers())
            .ExecuteAsync(Context(args: "ifillz"));

        result.Value.Should().Be("iFiLLz's account was created on August 31, 2021 (5 years ago).");
        await twitch
            .DidNotReceive()
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_login_gets_the_not_found_reply()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        Result<string> result = await BuiltinFor(db, twitch, FakeUsers())
            .ExecuteAsync(Context(args: "@nobody_here"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("@Stoney_Eagle Twitch has no user called nobody_here.");
        db.Users.Count().Should().Be(0);
    }

    [Fact]
    public async Task A_viewer_who_names_themselves_gets_the_caller_reply()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = TwitchId,
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
                AccountCreatedAt = Now.UtcDateTime.AddYears(-2),
            }
        );
        await db.SaveChangesAsync();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();

        Result<string> result = await BuiltinFor(db, twitch, FakeUsers())
            .ExecuteAsync(Context(args: "@Stoney_Eagle"));

        result.Value.Should().Be("Your account was created on August 31, 2024 (2 years ago).");
        await twitch
            .DidNotReceive()
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_target_lookup_gets_the_twitch_unavailable_reply()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<TwitchUser>>("timeout", "TIMEOUT"));

        Result<string> result = await BuiltinFor(db, twitch, FakeUsers())
            .ExecuteAsync(Context(args: "@ifillz"));

        result
            .Value.Should()
            .Be("@Stoney_Eagle Twitch did not answer just now — try again in a moment.");
    }

    [Fact]
    public async Task A_Kick_chatter_is_created_under_kick_and_never_sent_to_Helix()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        User row = new()
        {
            Username = Login,
            UsernameNormalized = Login,
            DisplayName = "Stoney_Eagle",
            AccountCreatedAt = null,
        };
        db.Users.Add(row);
        await db.SaveChangesAsync();

        IUserService users = FakeUsers();
        IUserIdentityService identities = Substitute.For<IUserIdentityService>();
        identities
            .ResolveUserAsync(
                AuthEnums.Platform.Kick,
                TwitchId,
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(row.Id));
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();

        AccountAgeBuiltin builtin = new(
            twitch,
            users,
            db,
            identities,
            TestMentionTargets.LoginsOnly(),
            TestBuiltinComposer.Create(),
            new FakeTimeProvider(Now)
        );

        Result<string> result = await builtin.ExecuteAsync(
            Context(platform: AuthEnums.Platform.Kick)
        );

        await users
            .Received(1)
            .GetOrCreateAsync(
                TwitchId,
                Login,
                "Stoney_Eagle",
                AuthEnums.Platform.Kick,
                Arg.Any<CancellationToken>()
            );
        await twitch
            .DidNotReceive()
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("could not be determined");
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
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestMentionTargets.LoginsOnly(),
            TestBuiltinComposer.Create(),
            new FakeTimeProvider(Now)
        );

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Your account was created on August 31, 2024 (2 years ago).");
    }

    [Fact]
    public async Task Sassy_tone_produces_the_sassy_variant_not_the_raw_hardcoded_string()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = TwitchId,
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
                AccountCreatedAt = Now.UtcDateTime.AddYears(-2),
            }
        );
        await db.SaveChangesAsync();

        AccountAgeBuiltin builtin = new(
            Substitute.For<ITwitchUsersApi>(),
            FakeUsers(),
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestMentionTargets.LoginsOnly(),
            TestBuiltinComposer.Create(),
            new FakeTimeProvider(Now)
        );

        Result<string> sassy = await builtin.ExecuteAsync(Context(PersonalityTone.Sassy));
        Result<string> informative = await builtin.ExecuteAsync(Context());

        string legacySentence = "Your account was created on August 31, 2024 (2 years ago).";
        sassy.Value.Should().NotBe(legacySentence);
        sassy.Value.Should().Contain("August 31, 2024").And.Contain("2 years");
        HashSet<string> sassyVariants =
        [
            .. ToneTemplateCatalog
                .Get(
                    PersonalityTone.Sassy,
                    BuiltinResponseSlots.AccountAge.Key,
                    BuiltinResponseSlots.AccountAge.Age
                )
                .Select(t =>
                    t.Replace("{user}", "Stoney_Eagle")
                        .Replace("{date}", "August 31, 2024")
                        .Replace("{age}", "2 years")
                ),
        ];
        sassyVariants.Should().Contain(sassy.Value);

        // The informative tone is the legacy sentence verbatim.
        informative.Value.Should().Be(legacySentence);
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

        AccountAgeBuiltin builtin = new(
            twitch,
            FakeUsers(),
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestMentionTargets.LoginsOnly(),
            TestBuiltinComposer.Create(),
            new FakeTimeProvider(Now)
        );

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Should()
            .Be("Your account was created on May 31, 2025 (1 year, 3 months ago).");
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

        AccountAgeBuiltin builtin = new(
            twitch,
            FakeUsers(),
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestMentionTargets.LoginsOnly(),
            TestBuiltinComposer.Create(),
            new FakeTimeProvider(Now)
        );

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue("the command always answers rather than going silent");
        result.Value.Should().Contain("try again");
    }
}
