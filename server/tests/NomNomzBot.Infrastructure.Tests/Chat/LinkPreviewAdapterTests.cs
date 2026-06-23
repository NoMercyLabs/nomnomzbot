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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Adapters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the link step (chat-decoration spec §3.5): when the channel has opted in AND the sender has standing, a url
/// word becomes a <c>link</c> fragment carrying its preview; with the feature off or the sender lacking standing the
/// step does not run at all (no outbound fetch is even attempted for an arbitrary viewer's link).
/// </summary>
public sealed class LinkPreviewAdapterTests
{
    private static ChatMessageFragment Text(string text) => new() { Type = "text", Text = text };

    private static ChatDecorationContext Context(bool standing, bool enabled, string text) =>
        new()
        {
            SenderHasPreviewStanding = standing,
            EnabledFeatures = enabled
                ? new HashSet<string> { "use_link_preview" }
                : new HashSet<string>(),
            Fragments = [Text(text)],
        };

    private static ILinkPreviewService PreviewReturning(LinkPreview? preview)
    {
        ILinkPreviewService service = Substitute.For<ILinkPreviewService>();
        service
            .FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(preview)));
        return service;
    }

    [Fact]
    public async Task Converts_a_url_to_a_link_fragment_with_its_preview()
    {
        ILinkPreviewService previews = PreviewReturning(
            new LinkPreview("example.com", "Title", "Desc", "https://img/x.png")
        );
        ChatDecorationContext context = Context(
            standing: true,
            enabled: true,
            "https://example.com/page"
        );

        await new LinkPreviewAdapter(previews).DecorateAsync(context);

        ChatMessageFragment fragment = context.Fragments.Should().ContainSingle().Subject;
        fragment.Type.Should().Be("link");
        fragment.LinkUrl.Should().Be("https://example.com/page");
        fragment.LinkPreview!.Title.Should().Be("Title");
        fragment.LinkPreview!.ImageUrl.Should().Be("https://img/x.png");
    }

    [Fact]
    public void Does_not_apply_when_the_feature_is_off()
    {
        LinkPreviewAdapter adapter = new(PreviewReturning(null));

        adapter
            .AppliesTo(Context(standing: true, enabled: false, "https://example.com"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Does_not_apply_without_sender_standing()
    {
        LinkPreviewAdapter adapter = new(PreviewReturning(null));

        adapter
            .AppliesTo(Context(standing: false, enabled: true, "https://example.com"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Does_not_apply_to_a_message_with_no_url()
    {
        LinkPreviewAdapter adapter = new(PreviewReturning(null));

        adapter
            .AppliesTo(Context(standing: true, enabled: true, "just some words"))
            .Should()
            .BeFalse();
    }
}
