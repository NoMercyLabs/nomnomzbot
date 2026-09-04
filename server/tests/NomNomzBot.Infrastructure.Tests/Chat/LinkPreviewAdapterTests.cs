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
            new("example.com", "Title", "Desc", "https://img/x.png")
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
    public async Task A_song_request_gets_the_preview_even_though_the_url_is_not_the_whole_message()
    {
        // The owner's actual case: "!sr <spotify url>". Twitch splits fragments around emotes and mentions,
        // never around urls, so this arrives as ONE text fragment. Requiring the whole fragment to parse as a
        // Uri meant this silently got nothing while a url pasted alone worked — which is exactly why the
        // song-request bubble showed raw command text instead of the track's OpenGraph card.
        ILinkPreviewService previews = PreviewReturning(
            new("open.spotify.com", "Track Name", "Artist", "https://i.scdn.co/art.jpg")
        );
        ChatDecorationContext context = Context(
            standing: true,
            enabled: true,
            "!sr https://open.spotify.com/track/abc123"
        );

        await new LinkPreviewAdapter(previews).DecorateAsync(context);

        context.Fragments.Should().HaveCount(2);
        context.Fragments[0].Type.Should().Be("text");
        context.Fragments[0].Text.Should().Be("!sr ");

        ChatMessageFragment link = context.Fragments[1];
        link.Type.Should().Be("link");
        link.LinkUrl.Should().Be("https://open.spotify.com/track/abc123");
        link.LinkPreview!.Title.Should().Be("Track Name");
        link.LinkPreview!.ImageUrl.Should().Be("https://i.scdn.co/art.jpg");
    }

    [Fact]
    public async Task A_url_with_words_on_both_sides_keeps_every_character_of_the_message()
    {
        // The fragments are rendered back to back by the overlay, so re-joining them must reproduce the
        // message EXACTLY. A splitter that rebuilt text from words would drop or double the spaces around
        // the link and quietly reflow every message that carries one.
        const string message = "check https://youtu.be/xyz out now";
        ILinkPreviewService previews = PreviewReturning(new("youtu.be", "Video", null, null));
        ChatDecorationContext context = Context(standing: true, enabled: true, message);

        await new LinkPreviewAdapter(previews).DecorateAsync(context);

        string.Concat(context.Fragments.Select(f => f.Text)).Should().Be(message);
        context.Fragments.Select(f => f.Type).Should().Equal("text", "link", "text");
        context.Fragments[1].LinkUrl.Should().Be("https://youtu.be/xyz");
    }

    [Fact]
    public async Task Two_urls_in_one_message_each_get_their_own_preview()
    {
        ILinkPreviewService previews = PreviewReturning(new("example.com", "T", null, null));
        ChatDecorationContext context = Context(
            standing: true,
            enabled: true,
            "https://a.example.com/1 and https://b.example.com/2"
        );

        await new LinkPreviewAdapter(previews).DecorateAsync(context);

        context
            .Fragments.Where(f => f.Type == "link")
            .Select(f => f.LinkUrl)
            .Should()
            .Equal("https://a.example.com/1", "https://b.example.com/2");
        await previews.Received(2).FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_message_with_no_url_is_left_completely_alone()
    {
        // Not a smoke check: the adapter now REBUILDS the fragment list, so a bug here would silently
        // replace every emote/mention fragment in an ordinary message with a plain text one.
        ILinkPreviewService previews = PreviewReturning(null);
        ChatDecorationContext context = new()
        {
            SenderHasPreviewStanding = true,
            EnabledFeatures = new HashSet<string> { "use_link_preview" },
            Fragments =
            [
                Text("hello "),
                new()
                {
                    Type = "emote",
                    Text = "Kappa",
                    EmoteId = "25",
                },
                Text(" world"),
            ],
        };

        await new LinkPreviewAdapter(previews).DecorateAsync(context);

        context.Fragments.Select(f => f.Type).Should().Equal("text", "emote", "text");
        context.Fragments[1].EmoteId.Should().Be("25");
        await previews.DidNotReceiveWithAnyArgs().FetchAsync(default!);
    }

    [Fact]
    public void Applies_to_a_url_embedded_in_a_sentence()
    {
        // AppliesTo gates whether the step runs at all. If it kept matching only whole-fragment urls, the
        // work above would never execute for the owner's case no matter how correct DecorateAsync is.
        LinkPreviewAdapter adapter = new(PreviewReturning(null));

        adapter
            .AppliesTo(
                Context(standing: true, enabled: true, "!sr https://open.spotify.com/track/a")
            )
            .Should()
            .BeTrue();
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
