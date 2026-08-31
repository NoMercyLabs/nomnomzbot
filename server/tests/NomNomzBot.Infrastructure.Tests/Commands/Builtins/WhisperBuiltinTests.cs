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
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!whisper &lt;user&gt; &lt;message&gt;</c> (legacy parity, S068c) proves the REAL side effect: the
/// target login is actually resolved to a Twitch id via <see cref="ITwitchUsersApi"/>, and the Twitch
/// <see cref="IPlatformDirectMessageSender"/> is actually invoked with that id and the exact message —
/// not merely "no exception". Also proves the usage/notfound copy is tone-styled (S069h).
/// </summary>
public sealed class WhisperBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009903");

    private static BuiltinCommandContext Context(
        string args,
        string personality = PersonalityTone.Informative
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "mod-1",
            TriggeringUserDisplayName = "SomeMod",
            Args = args,
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

    private static TwitchUser Viewer1() =>
        new(
            Id: "999",
            Login: "viewer1",
            DisplayName: "Viewer1",
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UtcNow
        );

    [Fact]
    public async Task Resolves_the_target_login_and_sends_the_exact_message_to_the_real_provider_user_id()
    {
        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        twitchUsers
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(logins => logins.Single() == "viewer1"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([Viewer1()]));

        IPlatformDirectMessageSender twitchSender = Substitute.For<IPlatformDirectMessageSender>();
        twitchSender.Provider.Returns(AuthEnums.Platform.Twitch);
        twitchSender
            .SendAsync(Broadcaster, "999", "hey stop that", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        WhisperBuiltin sut = new(twitchUsers, [twitchSender], FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Context("@viewer1 hey stop that"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Viewer1");

        await twitchSender
            .Received(1)
            .SendAsync(Broadcaster, "999", "hey stop that", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_login_never_calls_the_dm_sender()
    {
        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        twitchUsers
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        IPlatformDirectMessageSender twitchSender = Substitute.For<IPlatformDirectMessageSender>();
        twitchSender.Provider.Returns(AuthEnums.Platform.Twitch);

        WhisperBuiltin sut = new(twitchUsers, [twitchSender], FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Context("ghostuser hello"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Could not find");

        await twitchSender
            .DidNotReceive()
            .SendAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Missing_message_argument_never_looks_up_a_user()
    {
        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        WhisperBuiltin sut = new(twitchUsers, [], FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Context("viewer1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Usage");

        await twitchUsers
            .DidNotReceive()
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_usage_message_than_the_default_tone()
    {
        WhisperBuiltin sut = new(Substitute.For<ITwitchUsersApi>(), [], FakeComposer());

        Result<string> sassy = await sut.ExecuteAsync(Context("viewer1", PersonalityTone.Sassy));
        Result<string> informative = await sut.ExecuteAsync(
            Context("viewer1", PersonalityTone.Informative)
        );

        informative.Value.Should().Be("Usage: !whisper <user> <message>");
        sassy.Value.Should().NotBe(informative.Value);
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.Whisper.Key,
                BuiltinResponseSlots.Whisper.Usage
            )
            .Should()
            .Contain(sassy.Value);
    }

    [Fact]
    public async Task Sassy_tone_produces_a_different_not_found_message_than_the_default_tone()
    {
        ITwitchUsersApi twitchUsers = Substitute.For<ITwitchUsersApi>();
        twitchUsers
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));
        WhisperBuiltin sut = new(twitchUsers, [], FakeComposer());

        Result<string> sassy = await sut.ExecuteAsync(
            Context("ghostuser hello", PersonalityTone.Sassy)
        );
        Result<string> informative = await sut.ExecuteAsync(
            Context("ghostuser hello", PersonalityTone.Informative)
        );

        informative.Value.Should().Be("Could not find a Twitch user named \"ghostuser\".");
        sassy.Value.Should().NotBe(informative.Value);
    }
}
