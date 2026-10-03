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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!lurk</c>/<c>!unlurk</c> (legacy parity, S068a) flip <see cref="User.IsLurking"/> on the caller's
/// real row and confirm it in chat, in the channel's personality tone (S069a) — these prove the FLAG
/// ACTUALLY FLIPS on the persisted row, not just that a string came back.
/// </summary>
public sealed class LurkBuiltinsTests
{
    private const string TwitchId = "42";
    private const string Login = "stoney_eagle";

    private static BuiltinCommandContext Context(
        string personality = PersonalityTone.Informative,
        string? platform = null
    ) =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = TwitchId,
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = Login,
            Personality = personality,
            TriggeringPlatform = platform,
        };

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
        return new BuiltinResponseComposer(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            FakeChannelBuiltinReplies.None
        );
    }

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

        LurkBuiltin builtin = new(
            FakeUsers(),
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            FakeComposer()
        );

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("lurking");
        db.Users.Single(u => u.TwitchUserId == TwitchId).IsLurking.Should().BeTrue();
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
                IsLurking = false,
            }
        );
        await db.SaveChangesAsync();

        LurkBuiltin builtin = new(
            FakeUsers(),
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            FakeComposer()
        );

        Result<string> sassy = await builtin.ExecuteAsync(Context(PersonalityTone.Sassy));
        Result<string> informative = await builtin.ExecuteAsync(Context());

        string oldHardcodedString = "@Stoney_Eagle is now lurking. Enjoy the stream!";
        sassy.Value.Should().NotBe(oldHardcodedString);
        HashSet<string> sassyVariants =
        [
            .. ToneTemplateCatalog
                .Get(
                    PersonalityTone.Sassy,
                    BuiltinResponseSlots.Lurk.Key,
                    BuiltinResponseSlots.Lurk.Lurking
                )
                .Select(t => t.Replace("{user}", "Stoney_Eagle")),
        ];
        sassyVariants.Should().Contain(sassy.Value);

        // Default tone still reads exactly as it did before this slice (regression).
        informative.Value.Should().Be(oldHardcodedString);
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

        UnlurkBuiltin builtin = new(
            FakeUsers(),
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            FakeComposer()
        );

        Result<string> result = await builtin.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("no longer lurking");
        db.Users.Single(u => u.TwitchUserId == TwitchId).IsLurking.Should().BeFalse();
    }

    [Fact]
    public async Task An_override_of_lurk_lurking_does_not_change_lurk_notlurking()
    {
        Guid broadcaster = Guid.CreateVersion7();
        BuiltinCommandContext context = new()
        {
            BroadcasterId = broadcaster,
            TriggeringUserId = TwitchId,
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = Login,
        };
        FakeChannelBuiltinReplies replies = new FakeChannelBuiltinReplies().Set(
            broadcaster,
            BuiltinResponseSlots.Lurk.Key,
            BuiltinResponseSlots.Lurk.Lurking,
            "{user} vanishes into the shadows."
        );
        await using CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Users.Add(
            new User
            {
                TwitchUserId = TwitchId,
                Username = Login,
                UsernameNormalized = Login,
                DisplayName = "Stoney_Eagle",
            }
        );
        await db.SaveChangesAsync();

        Result<string> lurking = await new LurkBuiltin(
            FakeUsers(),
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestBuiltinComposer.Create(replies)
        ).ExecuteAsync(context);
        Result<string> back = await new UnlurkBuiltin(
            FakeUsers(),
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestBuiltinComposer.Create(replies)
        ).ExecuteAsync(context);

        lurking.Value.Should().Be("Stoney_Eagle vanishes into the shadows.");
        back.Value.Should().Be("@Stoney_Eagle is no longer lurking. Welcome back!");
    }

    [Fact]
    public async Task A_Kick_chatter_is_created_under_the_kick_provider_not_twitch()
    {
        IUserService users = FakeUsers();
        await using CommandsTestDbContext db = CommandsTestDbContext.New();

        await new LurkBuiltin(
            users,
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            FakeComposer()
        ).ExecuteAsync(Context(platform: AuthEnums.Platform.Kick));

        await users
            .Received(1)
            .GetOrCreateAsync(
                TwitchId,
                Login,
                "Stoney_Eagle",
                AuthEnums.Platform.Kick,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unresolvable_account_replies_from_its_own_slot_and_flips_nothing()
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
            .Returns(Result.Failure<UserDto>("lookup failed", "ACCOUNT_ERROR"));
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

        Result<string> result = await new LurkBuiltin(
            users,
            db,
            BuiltinTestIdentities.ResolvingTwitchUsersOf(db),
            TestBuiltinComposer.Create()
        ).ExecuteAsync(Context());

        result.Value.Should().Be("@Stoney_Eagle your account could not be resolved.");
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
