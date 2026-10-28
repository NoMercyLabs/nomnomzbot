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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Adapters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the chat-HTML step (legacy chat-HTML parity): only a subscriber-and-above sender, and only on the opt-in
/// <c>use_chat_html</c> feature, has inline HTML rendered — and even then every fragment is sanitised (script, event
/// handlers, and dangerous URL schemes removed) before it is emitted. The gate (<c>AppliesTo</c>) is the protection for
/// non-subscribers and disabled channels, so those cases are exercised the way the orchestrator runs the adapter.
/// </summary>
public sealed class HtmlFragmentAdapterTests
{
    private static ChatMessageFragment Text(string text) => new() { Type = "text", Text = text };

    private static ChatMessageFragment Emote(string code) =>
        new()
        {
            Type = "emote",
            Text = code,
            Emote = new(
                EmoteProvider.Twitch,
                "25",
                code,
                new Dictionary<string, string>
                {
                    ["1"] = $"https://cdn/{code}/1x",
                    ["2"] = $"https://cdn/{code}/2x",
                    ["3"] = $"https://cdn/{code}/3x",
                },
                Animated: false,
                ZeroWidth: false
            ),
        };

    private static ChatDecorationContext Context(
        bool standing,
        bool enabled,
        params ChatMessageFragment[] fragments
    ) =>
        new()
        {
            SenderMayRenderHtml = standing,
            EnabledFeatures = enabled
                ? new HashSet<string> { "use_chat_html" }
                : new HashSet<string>(),
            Fragments = [.. fragments],
        };

    // The real orchestrator over the real adapter; only the feature store, cache, user store and role
    // resolver (storage the decorator reads) are stand-ins.
    private static ChatMessageDecorator Decorator(bool featureOn)
    {
        IFeatureService features = Substitute.For<IFeatureService>();
        features
            .GetFeaturesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new List<FeatureStatusDto>
                    {
                        new("use_chat_html", "Chat HTML", "Inline HTML", featureOn, null, []),
                    }
                )
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
                        Guid.NewGuid().ToString(),
                        "viewer",
                        "Viewer",
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow
                    )
                )
            );
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));
        return new(
            [new HtmlFragmentAdapter()],
            features,
            Substitute.For<ICacheService>(),
            users,
            roles,
            Substitute.For<IChatLinkAllowlist>(),
            NullLogger<ChatMessageDecorator>.Instance
        );
    }

    private static ChatMessageReceivedEvent Message(bool subscriber, string text) =>
        new()
        {
            BroadcasterId = Guid.NewGuid(),
            MessageId = "msg-1",
            TwitchBroadcasterId = "chan-1",
            UserId = "viewer-1",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = text,
            Fragments = [Text(text)],
            Badges = [],
            IsSubscriber = subscriber,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    [Fact]
    public async Task Subscriber_with_the_feature_on_renders_inline_html_as_one_html_fragment()
    {
        ChatDecorationContext context = Context(
            standing: true,
            enabled: true,
            Text("<marquee>hi</marquee>")
        );
        HtmlFragmentAdapter adapter = new();

        adapter.AppliesTo(context).Should().BeTrue();
        await adapter.DecorateAsync(context);

        ChatMessageFragment fragment = context.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("html");
        fragment.Text.Should().Contain("<marquee>").And.Contain("hi");
    }

    [Fact]
    public async Task Dangerous_markup_is_stripped_before_it_becomes_an_html_fragment()
    {
        ChatDecorationContext context = Context(
            standing: true,
            enabled: true,
            Text("<div><script>alert('xss')</script><img src=x onerror=alert(1)>hi</div>")
        );

        await new HtmlFragmentAdapter().DecorateAsync(context);

        ChatMessageFragment fragment = context.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("html");
        // The security guarantee: the executable payload is gone, the benign content stays.
        fragment.Text.Should().NotContainEquivalentOf("script");
        fragment.Text.Should().NotContainEquivalentOf("onerror");
        fragment.Text.Should().NotContainEquivalentOf("alert");
        fragment.Text.Should().Contain("hi");
    }

    [Fact]
    public async Task A_non_subscriber_gets_no_html_fragment()
    {
        DecoratedChatMessage decorated = await Decorator(featureOn: true)
            .DecorateAsync(Message(subscriber: false, "<marquee>hi</marquee>"));

        ChatMessageFragment fragment = decorated.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("text");
        fragment.Text.Should().Be("<marquee>hi</marquee>");
    }

    [Fact]
    public async Task A_subscriber_through_the_decorator_gets_the_html_fragment_when_the_feature_is_on()
    {
        DecoratedChatMessage decorated = await Decorator(featureOn: true)
            .DecorateAsync(Message(subscriber: true, "<marquee>hi</marquee>"));

        ChatMessageFragment fragment = decorated.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("html");
        fragment.Text.Should().Contain("<marquee>");
    }

    [Fact]
    public async Task The_feature_being_off_yields_no_html_fragment()
    {
        DecoratedChatMessage decorated = await Decorator(featureOn: false)
            .DecorateAsync(Message(subscriber: true, "<marquee>hi</marquee>"));

        ChatMessageFragment fragment = decorated.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("text");
        fragment.Text.Should().Be("<marquee>hi</marquee>");
    }

    [Fact]
    public async Task A_tag_spanning_text_and_emote_fragments_becomes_one_html_fragment_with_an_img()
    {
        ChatDecorationContext context = Context(
            standing: true,
            enabled: true,
            Text("<marquee> "),
            Emote("Kappa"),
            Text(" </marquee>")
        );
        HtmlFragmentAdapter adapter = new();

        adapter.AppliesTo(context).Should().BeTrue();
        await adapter.DecorateAsync(context);

        ChatMessageFragment fragment = context.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("html");
        fragment.Text.Should().Contain("<marquee>").And.Contain("<img");
    }

    [Fact]
    public async Task An_emote_in_a_span_is_marked_as_an_emote_and_the_senders_own_image_is_not()
    {
        ChatDecorationContext context = Context(
            standing: true,
            enabled: true,
            Text(
                @"<marquee><img width=""400"" src=""https://media.giphy.com/media/x/giphy.gif""> "
            ),
            Emote("Kappa"),
            Text(" </marquee>")
        );
        HtmlFragmentAdapter adapter = new();

        await adapter.DecorateAsync(context);

        string html = context.Fragments.Should().ContainSingle().Subject.Text;
        html.Should()
            .Contain(@"<img src=""https://cdn/Kappa/3x"" alt=""Kappa"" class=""emote"">")
            .And.Contain(@"<img width=""400"" src=""https://media.giphy.com/media/x/giphy.gif"">");
    }

    [Fact]
    public async Task Several_marquees_with_an_emote_each_stay_in_one_html_fragment()
    {
        ChatDecorationContext context = Context(
            standing: true,
            enabled: true,
            Text(@"<div style=""width:400px""><marquee direction=""right"" width=""400"">"),
            Emote("Top"),
            Text(@"</marquee><marquee direction=""up"" width=""28"" height=""144"">"),
            Emote("Left"),
            Text(@"</marquee><marquee direction=""down"" width=""28"" height=""144"">"),
            Emote("Right"),
            Text(@"</marquee><marquee direction=""left"" width=""400"">"),
            Emote("Bottom"),
            Text("</marquee></div>")
        );
        HtmlFragmentAdapter adapter = new();

        await adapter.DecorateAsync(context);

        string html = context.Fragments.Should().ContainSingle().Subject.Text;
        html.Split("<img").Length.Should().Be(5, "four emotes, one img each");
        html.Should()
            .Contain(@"direction=""up""")
            .And.Contain(@"direction=""left""")
            .And.EndWith("</div>");
    }
}
