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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Adapters;

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
            Emote = new ChatEmote(
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
            SenderHasPreviewStanding = standing,
            EnabledFeatures = enabled
                ? new HashSet<string> { "use_chat_html" }
                : new HashSet<string>(),
            Fragments = [.. fragments],
        };

    // Mirrors the orchestrator: an adapter only runs when its gate passes.
    private static async Task RunGated(HtmlFragmentAdapter adapter, ChatDecorationContext context)
    {
        if (adapter.AppliesTo(context))
            await adapter.DecorateAsync(context);
    }

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
        ChatMessageFragment original = Text("<marquee>hi</marquee>");
        ChatDecorationContext context = Context(standing: false, enabled: true, original);
        HtmlFragmentAdapter adapter = new();

        adapter.AppliesTo(context).Should().BeFalse();
        await RunGated(adapter, context);

        context.Fragments.Should().ContainSingle().Which.Should().BeSameAs(original);
        context.Fragments.Should().NotContain(fragment => fragment.Type == "html");
    }

    [Fact]
    public async Task The_feature_being_off_yields_no_html_fragment()
    {
        ChatMessageFragment original = Text("<marquee>hi</marquee>");
        ChatDecorationContext context = Context(standing: true, enabled: false, original);
        HtmlFragmentAdapter adapter = new();

        adapter.AppliesTo(context).Should().BeFalse();
        await RunGated(adapter, context);

        context.Fragments.Should().ContainSingle().Which.Should().BeSameAs(original);
        context.Fragments.Should().NotContain(fragment => fragment.Type == "html");
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
}
