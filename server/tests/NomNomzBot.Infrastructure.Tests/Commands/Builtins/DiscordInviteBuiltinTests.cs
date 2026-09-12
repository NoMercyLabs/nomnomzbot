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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// <c>!discord</c> (legacy parity, S068/§C7) proves the REAL side effects: a channel with no active guild
/// link never calls Discord at all; an active link with no bot-postable channel never invents an invite; and
/// only a channel the bot can actually post in gets a real <see cref="IDiscordBotGateway.CreateChannelInviteAsync"/>
/// call, whose returned code is exactly what the reply carries — never a fabricated link.
/// </summary>
public sealed class DiscordInviteBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000d001");

    private static BuiltinCommandContext Context() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "viewer-1",
            TriggeringUserDisplayName = "Viewer",
            Args = "",
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

    private static DiscordGuildConnectionDto ActiveConnection() =>
        new(
            Guid.NewGuid(),
            Broadcaster,
            "guild-1",
            "My Server",
            BotInstalled: true,
            ServerConsentStatus: "approved",
            ApprovedByDiscordUserId: "admin-1",
            ApprovedAt: DateTime.UtcNow,
            StreamerEnabled: true,
            IsLinkActive: true,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );

    [Fact]
    public async Task No_active_guild_link_never_calls_the_gateway()
    {
        IDiscordGuildService guilds = Substitute.For<IDiscordGuildService>();
        guilds
            .GetConnectionsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<DiscordGuildConnectionDto>>([]));
        IDiscordBotGateway gateway = Substitute.For<IDiscordBotGateway>();

        DiscordInviteBuiltin sut = new(guilds, gateway, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("linked");

        await gateway
            .DidNotReceive()
            .GetPostableGuildChannelsAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await gateway
            .DidNotReceive()
            .CreateChannelInviteAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task No_postable_channel_never_creates_an_invite()
    {
        IDiscordGuildService guilds = Substitute.For<IDiscordGuildService>();
        guilds
            .GetConnectionsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<DiscordGuildConnectionDto>>([ActiveConnection()])
            );
        IDiscordBotGateway gateway = Substitute.For<IDiscordBotGateway>();
        gateway
            .GetPostableGuildChannelsAsync(Broadcaster, "guild-1", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<DiscordPostableChannelDto>>([
                    new("chan-1", "general", 0, null, 0, false, "DISCORD_MISSING_PERMISSION", "no"),
                ])
            );

        DiscordInviteBuiltin sut = new(guilds, gateway, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("isn't available");

        await gateway
            .DidNotReceive()
            .CreateChannelInviteAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Postable_channel_creates_a_real_invite_and_replies_with_its_exact_code()
    {
        IDiscordGuildService guilds = Substitute.For<IDiscordGuildService>();
        guilds
            .GetConnectionsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<DiscordGuildConnectionDto>>([ActiveConnection()])
            );
        IDiscordBotGateway gateway = Substitute.For<IDiscordBotGateway>();
        gateway
            .GetPostableGuildChannelsAsync(Broadcaster, "guild-1", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<DiscordPostableChannelDto>>([
                    new("chan-1", "general", 0, null, 0, true, null, null),
                ])
            );
        gateway
            .CreateChannelInviteAsync(Broadcaster, "chan-1", Arg.Any<CancellationToken>())
            .Returns(Result.Success("xyz789"));

        DiscordInviteBuiltin sut = new(guilds, gateway, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("discord.gg/xyz789");

        await gateway
            .Received(1)
            .CreateChannelInviteAsync(Broadcaster, "chan-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_invite_creation_never_fabricates_a_link()
    {
        IDiscordGuildService guilds = Substitute.For<IDiscordGuildService>();
        guilds
            .GetConnectionsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<DiscordGuildConnectionDto>>([ActiveConnection()])
            );
        IDiscordBotGateway gateway = Substitute.For<IDiscordBotGateway>();
        gateway
            .GetPostableGuildChannelsAsync(Broadcaster, "guild-1", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<DiscordPostableChannelDto>>([
                    new("chan-1", "general", 0, null, 0, true, null, null),
                ])
            );
        gateway
            .CreateChannelInviteAsync(Broadcaster, "chan-1", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>("Discord request failed.", "DISCORD_ERROR"));

        DiscordInviteBuiltin sut = new(guilds, gateway, FakeComposer());

        Result<string> result = await sut.ExecuteAsync(Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain("discord.gg");
        result.Value.Should().Contain("isn't available");
    }
}
