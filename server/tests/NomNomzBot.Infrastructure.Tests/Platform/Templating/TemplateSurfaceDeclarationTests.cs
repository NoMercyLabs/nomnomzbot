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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// A helper is declared only on the surfaces whose caller seeds its data: a timer has no viewer and no chat
/// message, and only the events that carry a message, bits or a platform declare those helpers.
/// </summary>
public sealed class TemplateSurfaceDeclarationTests
{
    private static ITemplateHelperRegistry Registry()
    {
        ServiceCollection services = new();
        services.AddTemplateHelpers(typeof(TemplateResolver).Assembly);
        return services.BuildServiceProvider().GetRequiredService<ITemplateHelperRegistry>();
    }

    [Theory]
    [InlineData("platform.user")]
    [InlineData("platform.user.name")]
    [InlineData("platform.user.icon")]
    [InlineData("chat.message")]
    [InlineData("chat.message.length")]
    [InlineData("chat.message.noCommand")]
    [InlineData("chat.message.bits")]
    [InlineData("chat.mention.user")]
    public void A_timer_rejects_a_helper_that_needs_a_viewer_or_a_chat_message(string key)
    {
        TemplateHelperValidator validator = new(Registry());

        Result result = validator.Validate("{" + key + "}", TemplateHelperContext.Timer);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain(key);
    }

    [Fact]
    public void The_go_live_discord_post_rejects_helpers_with_no_viewer_platform_or_bits()
    {
        TemplateHelperValidator validator = new(Registry());

        validator
            .Validate("{platform.user} {chat.message.bits}", TemplateHelperContext.Discord)
            .IsFailure.Should()
            .BeTrue();
        validator
            .Validate("{chat.message} {chat.mention.user}", TemplateHelperContext.Discord)
            .IsSuccess.Should()
            .BeTrue("a cheer or resub post seeds the message and the viewer");
    }

    [Fact]
    public void A_cheer_declares_message_bits_and_platform_but_a_raid_declares_none_of_them()
    {
        ITemplateHelperRegistry registry = Registry();
        string[] keys =
        [
            "chat.message",
            "chat.message.bits",
            "user.provider",
            "platform.user.name",
        ];

        List<string> cheer =
        [
            .. registry.ForContext(TemplateHelperContext.EventResponse, "channel.cheer").Select(e => e.Key),
        ];
        List<string> raid =
        [
            .. registry.ForContext(TemplateHelperContext.EventResponse, "channel.raid").Select(e => e.Key),
        ];

        cheer.Should().Contain(keys);
        raid.Should().NotContain(keys);
    }

    [Fact]
    public void A_follow_declares_the_platform_but_no_message()
    {
        List<string> follow =
        [
            .. Registry()
                .ForContext(TemplateHelperContext.EventResponse, "channel.follow")
                .Select(e => e.Key),
        ];

        follow.Should().Contain("user.provider");
        follow.Should().NotContain(["chat.message", "chat.message.bits"]);
    }

    [Fact]
    public void The_viewer_platform_is_seeded_only_for_an_event_that_names_a_viewer_and_its_platform()
    {
        Dictionary<string, string> follow = new()
        {
            ["provider"] = "kick",
            ["user.id"] = "9",
        };
        Dictionary<string, string> anonymous = new() { ["provider"] = "twitch", ["user.id"] = "" };
        Dictionary<string, string> noPlatform = new() { ["user.id"] = "9" };

        TemplateSubject.SeedUserPlatform(follow);
        TemplateSubject.SeedUserPlatform(anonymous);
        TemplateSubject.SeedUserPlatform(noPlatform);

        follow["user.provider"].Should().Be("kick");
        anonymous.Should().NotContainKey("user.provider");
        noPlatform.Should().NotContainKey("user.provider");
    }
}
