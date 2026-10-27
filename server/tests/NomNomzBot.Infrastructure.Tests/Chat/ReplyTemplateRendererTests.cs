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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A reply template is resolved once per place it is shown: plain for the platform chat, HTML-encoded
/// variables for an HTML overlay line, so a viewer's name or arguments can never inject markup.
/// </summary>
public sealed class ReplyTemplateRendererTests
{
    private const string Template = "<marquee>CHOO CHOO {user}</marquee>";
    private static readonly Guid Channel = Guid.Parse("0197d6a0-0000-7000-8000-000000000001");

    private static Dictionary<string, string> Variables(string user) =>
        new(StringComparer.OrdinalIgnoreCase) { ["user"] = user };

    [Fact]
    public async Task Chat_only_resolves_once_for_chat_and_has_no_overlay_text()
    {
        SubstitutingTemplateResolver resolver = new();

        RenderedReply rendered = await ReplyTemplateRenderer.RenderAsync(
            resolver,
            Template,
            Variables("Stoney"),
            Channel,
            ReplyDelivery.ChatOnly
        );

        rendered.ChatText.Should().Be("<marquee>CHOO CHOO Stoney</marquee>");
        rendered.OverlayText.Should().BeNull();
        resolver.Seeds.Should().HaveCount(1);
        ChatMentionName.IsChatRender(resolver.Seeds[0]).Should().BeTrue();
    }

    [Fact]
    public async Task Overlay_with_html_encodes_every_variable_and_leaves_the_chat_text_empty()
    {
        SubstitutingTemplateResolver resolver = new();

        RenderedReply rendered = await ReplyTemplateRenderer.RenderAsync(
            resolver,
            Template,
            Variables("<script>x</script>"),
            Channel,
            new ReplyDelivery(ReplyTarget.Overlay, Html: true)
        );

        rendered.ChatText.Should().BeEmpty();
        rendered
            .OverlayText.Should()
            .Be("<marquee>CHOO CHOO &lt;script&gt;x&lt;/script&gt;</marquee>");
        resolver.Seeds.Should().HaveCount(1);
        ChatMentionName.IsChatRender(resolver.Seeds[0]).Should().BeFalse();
    }

    [Fact]
    public async Task Both_with_html_keeps_the_chat_text_raw_and_encodes_only_the_overlay_text()
    {
        SubstitutingTemplateResolver resolver = new();

        RenderedReply rendered = await ReplyTemplateRenderer.RenderAsync(
            resolver,
            Template,
            Variables("A&B"),
            Channel,
            new ReplyDelivery(ReplyTarget.Both, Html: true)
        );

        rendered.ChatText.Should().Be("<marquee>CHOO CHOO A&B</marquee>");
        rendered.OverlayText.Should().Be("<marquee>CHOO CHOO A&amp;B</marquee>");
        resolver.Seeds.Should().HaveCount(2);
    }

    [Fact]
    public async Task Both_without_html_shows_the_same_resolved_text_in_both_places_after_one_resolve()
    {
        SubstitutingTemplateResolver resolver = new();

        RenderedReply rendered = await ReplyTemplateRenderer.RenderAsync(
            resolver,
            "hi {user}",
            Variables("A&B"),
            Channel,
            new ReplyDelivery(ReplyTarget.Both, Html: false)
        );

        rendered.ChatText.Should().Be("hi A&B");
        rendered.OverlayText.Should().Be("hi A&B");
        resolver.Seeds.Should().HaveCount(1);
    }

    [Fact]
    public async Task Overlay_without_html_resolves_plain_variables_for_the_overlay_only()
    {
        SubstitutingTemplateResolver resolver = new();

        RenderedReply rendered = await ReplyTemplateRenderer.RenderAsync(
            resolver,
            "hi {user}",
            Variables("A&B"),
            Channel,
            new ReplyDelivery(ReplyTarget.Overlay, Html: false)
        );

        rendered.ChatText.Should().BeEmpty();
        rendered.OverlayText.Should().Be("hi A&B");
        resolver.Seeds.Should().HaveCount(1);
    }

    [Fact]
    public async Task None_resolves_nothing()
    {
        SubstitutingTemplateResolver resolver = new();

        RenderedReply rendered = await ReplyTemplateRenderer.RenderAsync(
            resolver,
            Template,
            Variables("Stoney"),
            Channel,
            new ReplyDelivery(ReplyTarget.None)
        );

        rendered.ChatText.Should().BeEmpty();
        rendered.OverlayText.Should().BeNull();
        resolver.Seeds.Should().BeEmpty();
    }
}
