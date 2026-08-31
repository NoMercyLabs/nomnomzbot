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
        string personality = PersonalityTone.Informative
    ) =>
        new()
        {
            BroadcasterId = Guid.CreateVersion7(),
            TriggeringUserId = TwitchId,
            TriggeringUserDisplayName = "Stoney_Eagle",
            TriggeringUserLogin = Login,
            Personality = personality,
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
        return new BuiltinResponseComposer(resolver);
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

        LurkBuiltin builtin = new(FakeUsers(), db, FakeComposer());

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

        LurkBuiltin builtin = new(FakeUsers(), db, FakeComposer());

        Result<string> sassy = await builtin.ExecuteAsync(Context(PersonalityTone.Sassy));
        Result<string> informative = await builtin.ExecuteAsync(
            Context(PersonalityTone.Informative)
        );

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

        UnlurkBuiltin builtin = new(FakeUsers(), db, FakeComposer());

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
