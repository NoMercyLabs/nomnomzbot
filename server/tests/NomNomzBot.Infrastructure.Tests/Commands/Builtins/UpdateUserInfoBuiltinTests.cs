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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!update</c> re-reads a viewer's Twitch profile so a rename or a new avatar shows everywhere. These
/// hold what it must actually DO (write the fresh profile onto the row) and what it must SAY when the
/// platform is the thing that failed — reporting a Helix timeout as "no such user" tells a viewer their
/// account is gone when Twitch simply did not answer. Also proves the "not found" copy is tone-styled
/// (S069h).
/// </summary>
public sealed class UpdateUserInfoBuiltinTests
{
    private const string Login = "stoney_eagle";

    private static IBuiltinResponseComposer FakeComposer()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
        return new BuiltinResponseComposer(resolver);
    }

    private static TwitchUser Profile(string avatar) =>
        new(
            Id: "42",
            Login: Login,
            DisplayName: "Stoney_Eagle",
            Type: "",
            BroadcasterType: "affiliate",
            Description: "just a streamer",
            ProfileImageUrl: avatar,
            OfflineImageUrl: "https://cdn/offline.png",
            ViewCount: 0,
            CreatedAt: new DateTimeOffset(2019, 3, 14, 8, 0, 0, TimeSpan.Zero)
        );

    private static BuiltinCommandContext Context(
        string args = "",
        int roleLevel = 0,
        string personality = PersonalityTone.Informative
    ) =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = "42",
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = Login,
            RoleLevel = roleLevel,
            Args = args,
            Personality = personality,
        };

    [Fact]
    public async Task A_self_update_writes_the_fresh_profile_onto_the_row()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = "42",
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
                ProfileImageUrl = "https://cdn/avatar-v1.png",
            }
        );
        await db.SaveChangesAsync();

        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([Profile("https://cdn/avatar-v2.png")])
            );

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

        UpdateUserInfoBuiltin builtin = new(twitch, users, db, FakeComposer());

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Stoney_Eagle");
        // The point of the command: the row actually carries the new avatar afterwards.
        User row = db.Users.Single(u => u.TwitchUserId == "42");
        row.ProfileImageUrl.Should().Be("https://cdn/avatar-v2.png");
        row.ProfileRefreshedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_platform_failure_is_reported_as_a_platform_failure_not_as_a_missing_user()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<TwitchUser>>(
                    "The operation didn't complete within the allowed timeout.",
                    "TIMEOUT"
                )
            );

        UpdateUserInfoBuiltin builtin = new(
            twitch,
            Substitute.For<IUserService>(),
            db,
            FakeComposer()
        );

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue("the command always answers rather than going silent");
        result
            .Value.Should()
            .NotContain(
                "Could not find",
                "a Twitch timeout must never be reported as the viewer not existing"
            );
        result.Value.Should().Contain("try again");
    }

    [Fact]
    public async Task Updating_someone_else_is_refused_below_moderator_and_says_why()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();

        UpdateUserInfoBuiltin builtin = new(
            twitch,
            Substitute.For<IUserService>(),
            db,
            FakeComposer()
        );

        Result<string> result = await builtin.ExecuteAsync(Context("@someone_else", roleLevel: 0));

        result.Value.Should().Contain("only update your own info");
        await twitch.DidNotReceiveWithAnyArgs().GetUsersByLoginsAsync(default!);
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_not_found_message_than_the_default_tone()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        UpdateUserInfoBuiltin builtin = new(
            twitch,
            Substitute.For<IUserService>(),
            db,
            FakeComposer()
        );

        Result<string> sassy = await builtin.ExecuteAsync(
            Context(personality: PersonalityTone.Sassy)
        );
        Result<string> informative = await builtin.ExecuteAsync(
            Context(personality: PersonalityTone.Informative)
        );

        informative.Value.Should().Be($"Could not find user '{Login}' on Twitch.");
        sassy.Value.Should().NotBe(informative.Value);
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.NotFound
            )
            .Select(t => t.Replace("{user}", Login))
            .Should()
            .Contain(sassy.Value);
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_twitch_unavailable_message_than_the_default_tone()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<TwitchUser>>("TIMEOUT", "TIMEOUT"));

        UpdateUserInfoBuiltin builtin = new(
            twitch,
            Substitute.For<IUserService>(),
            db,
            FakeComposer()
        );

        Result<string> sassy = await builtin.ExecuteAsync(
            Context(personality: PersonalityTone.Sassy)
        );
        Result<string> informative = await builtin.ExecuteAsync(
            Context(personality: PersonalityTone.Informative)
        );

        informative
            .Value.Should()
            .Be("@Stoney_Eagle Twitch did not answer just now — try again in a moment.");
        sassy.Value.Should().NotBe(informative.Value);
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.TwitchUnavailable
            )
            .Select(t => t.Replace("{user}", "Stoney_Eagle"))
            .Should()
            .Contain(sassy.Value);
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_update_failed_message_than_the_default_tone()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([Profile("https://cdn/avatar-v2.png")])
            );

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<UserDto>("db write failed", "DB_ERROR"));

        UpdateUserInfoBuiltin builtin = new(twitch, users, db, FakeComposer());

        Result<string> sassy = await builtin.ExecuteAsync(
            Context(personality: PersonalityTone.Sassy)
        );
        Result<string> informative = await builtin.ExecuteAsync(
            Context(personality: PersonalityTone.Informative)
        );

        informative.Value.Should().Be("Something went wrong updating Stoney_Eagle.");
        sassy.Value.Should().NotBe(informative.Value);
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.UpdateFailed
            )
            .Select(t => t.Replace("{user}", "Stoney_Eagle"))
            .Should()
            .Contain(sassy.Value);
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_login_unresolved_message_than_the_default_tone()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();

        UpdateUserInfoBuiltin builtin = new(
            twitch,
            Substitute.For<IUserService>(),
            db,
            FakeComposer()
        );

        BuiltinCommandContext EmptyLoginContext(string personality) =>
            new()
            {
                BroadcasterId = Guid.CreateVersion7(),
                TriggeringUserId = "42",
                TriggeringUserDisplayName = "Stoney_Eagle",
                TriggeringUserLogin = string.Empty,
                RoleLevel = 0,
                Args = string.Empty,
                Personality = personality,
            };

        Result<string> sassy = await builtin.ExecuteAsync(EmptyLoginContext(PersonalityTone.Sassy));
        Result<string> informative = await builtin.ExecuteAsync(
            EmptyLoginContext(PersonalityTone.Informative)
        );

        informative.Value.Should().Be("@Stoney_Eagle could not resolve your Twitch login.");
        sassy.Value.Should().NotBe(informative.Value);
        await twitch.DidNotReceiveWithAnyArgs().GetUsersByLoginsAsync(default!);
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.LoginUnresolved
            )
            .Select(t => t.Replace("{user}", "Stoney_Eagle"))
            .Should()
            .Contain(sassy.Value);
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_own_info_only_message_than_the_default_tone()
    {
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();

        UpdateUserInfoBuiltin builtin = new(
            twitch,
            Substitute.For<IUserService>(),
            db,
            FakeComposer()
        );

        Result<string> sassy = await builtin.ExecuteAsync(
            Context("@someone_else", roleLevel: 0, personality: PersonalityTone.Sassy)
        );
        Result<string> informative = await builtin.ExecuteAsync(
            Context("@someone_else", roleLevel: 0, personality: PersonalityTone.Informative)
        );

        informative
            .Value.Should()
            .Be("@Stoney_Eagle you can only update your own info, or be a mod to update others.");
        sassy.Value.Should().NotBe(informative.Value);
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.UpdateUserInfo.Key,
                BuiltinResponseSlots.UpdateUserInfo.OwnInfoOnly
            )
            .Select(t => t.Replace("{user}", "Stoney_Eagle"))
            .Should()
            .Contain(sassy.Value);
    }
}
