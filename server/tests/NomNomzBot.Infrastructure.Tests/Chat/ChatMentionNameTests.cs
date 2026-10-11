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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// S-CHAT-MENTION-LOGIN: Twitch links an @mention only by login, so chat text names a viewer by login
/// unless the display name is the login apart from letter case. Speech keeps the display name.
/// </summary>
public sealed class ChatMentionNameTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000d002");

    private static TemplateResolver BuildResolver()
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(ViewerDataTestDbContext.New());
        ServiceProvider provider = services.BuildServiceProvider();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        return new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static Dictionary<string, string> Viewer(string display, string login) =>
        new() { ["user"] = display, ["user.name"] = login };

    [Fact]
    public async Task Chat_line_names_a_non_latin_viewer_by_login_so_the_mention_links()
    {
        string line = await BuildResolver()
            .ResolveAsync(
                "@{user}, you pulled off a clean swipe",
                ChatMentionName.ForChat(Viewer("케이라라", "keiraravt")),
                Channel
            );

        line.Should().Be("@keiraravt, you pulled off a clean swipe");
    }

    [Fact]
    public async Task Spoken_line_keeps_the_display_name()
    {
        string line = await BuildResolver()
            .ResolveAsync(
                "@{user}, you pulled off a clean swipe",
                Viewer("케이라라", "keiraravt"),
                Channel
            );

        line.Should().Be("@케이라라, you pulled off a clean swipe");
    }

    [Fact]
    public async Task Chat_line_keeps_a_latin_display_name_that_only_differs_in_case()
    {
        string line = await BuildResolver()
            .ResolveAsync(
                "Hi {user}!",
                ChatMentionName.ForChat(Viewer("MoehDev", "moehdev")),
                Channel
            );

        line.Should().Be("Hi MoehDev!");
    }

    [Fact]
    public async Task Chat_line_names_a_non_latin_target_by_the_typed_login()
    {
        Dictionary<string, string> seed = ChatMentionName.ForChat(
            Viewer("Stoney_Eagle", "stoney_eagle")
        );
        seed["target"] = "keiraravt";
        seed["target.id"] = "1";
        seed["target.name"] = "케이라라";
        seed["target.displayname"] = "케이라라";

        string line = await BuildResolver()
            .ResolveAsync("{target.displayname} is being escorted off", seed, Channel);

        line.Should().Be("keiraravt is being escorted off");
    }

    [Theory]
    [InlineData("케이라라", "keiraravt", "keiraravt")]
    [InlineData("MoehDev", "moehdev", "MoehDev")]
    [InlineData("Stoney_Eagle", "stoney_eagle", "Stoney_Eagle")]
    public void For_returns_the_display_name_only_when_it_is_the_login_apart_from_case(
        string display,
        string login,
        string expected
    ) => ChatMentionName.For(display, login).Should().Be(expected);

    [Theory]
    [InlineData("Go follow @keiraravt now!", "케이라라", "keiraravt", "Go follow @케이라라 now!")]
    [InlineData(
        "keiraravt, keiraravt2 and KeiraRavt",
        "케이라라",
        "keiraravt",
        "케이라라, keiraravt2 and 케이라라"
    )]
    [InlineData("Hi MoehDev!", "MoehDev", "moehdev", "Hi MoehDev!")]
    [InlineData(
        "Go follow twitch.tv/keiraravt!",
        "케이라라",
        "keiraravt",
        "Go follow twitch.tv/keiraravt!"
    )]
    public void Spoken_form_puts_the_display_name_back_where_the_chat_line_carries_the_login(
        string chatLine,
        string display,
        string login,
        string expected
    ) => ChatMentionName.ToSpoken(chatLine, display, login).Should().Be(expected);

    [Fact]
    public void Auto_spoken_reply_keeps_the_triggering_users_display_name()
    {
        Dictionary<string, string> variables = new()
        {
            ["user"] = "케이라라",
            ["user.name"] = "keiraravt",
        };

        string spoken = ChatMentionName.ToSpoken("@keiraravt, nice swipe!", variables);

        spoken.Should().Be("@케이라라, nice swipe!");
    }

    [Fact]
    public void For_falls_back_to_the_display_name_when_no_login_is_known()
    {
        ChatMentionName.For("케이라라", null).Should().Be("케이라라");
        ChatMentionName.For("케이라라", "").Should().Be("케이라라");
    }
}
