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
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.Adapters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the standing behind chat HTML and link previews is permittable, not Twitch-only: a viewer with no badge
/// but a <c>chat:html:render</c> / <c>chat:link:preview</c> capability (a <c>!permit</c> grant, or a resolved level
/// that meets the action) gets the same decoration a subscriber gets. A badge-less viewer with no grant still gets
/// none, and the channel feature switch still wins over any standing. The resolver is only consulted when the
/// badge check fails, and only for the step the message can trigger.
/// </summary>
public sealed class ChatDecorationPermitStandingTests
{
    private static readonly Guid Channel = Guid.CreateVersion7();
    private static readonly Guid ViewerUser = Guid.CreateVersion7();
    private const string TwitchUserId = "u-viewer";

    [Fact]
    public async Task A_badge_less_viewer_holding_the_html_capability_has_inline_html_rendered()
    {
        IRoleResolver roles = Roles(html: true, links: false);
        ChatMessageDecorator decorator = Decorator(
            [new HtmlFragmentAdapter()],
            roles,
            ("use_chat_html", true)
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(Event("<b>hi</b>"));

        ChatMessageFragment fragment = result.Fragments.Should().ContainSingle().Which;
        fragment.Type.Should().Be("html");
        fragment.Text.Should().Be("<b>hi</b>");
    }

    [Fact]
    public async Task A_badge_less_viewer_with_no_grant_still_gets_no_html_and_the_resolver_was_asked_once()
    {
        IRoleResolver roles = Roles(html: false, links: false);
        ChatMessageDecorator decorator = Decorator(
            [new HtmlFragmentAdapter()],
            roles,
            ("use_chat_html", true)
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(Event("<b>hi</b>"));

        ChatMessageFragment fragment = result.Fragments.Should().ContainSingle().Which;
        fragment.Type.Should().Be("text");
        fragment.Text.Should().Be("<b>hi</b>");
        await roles
            .Received(1)
            .HasCapabilityAsync(
                ViewerUser,
                Channel,
                ChatDecorationCapabilities.RenderHtml,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_feature_switch_off_blocks_html_even_for_a_capability_holder_without_asking_the_resolver()
    {
        IRoleResolver roles = Roles(html: true, links: true);
        ChatMessageDecorator decorator = Decorator(
            [new HtmlFragmentAdapter()],
            roles,
            ("use_chat_html", false)
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(Event("<b>hi</b>"));

        ChatMessageFragment fragment = result.Fragments.Should().ContainSingle().Which;
        fragment.Type.Should().Be("text");
        fragment.Text.Should().Be("<b>hi</b>");
        await roles
            .DidNotReceive()
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_subscriber_badge_unlocks_html_without_consulting_the_resolver()
    {
        IRoleResolver roles = Roles(html: false, links: false);
        ChatMessageDecorator decorator = Decorator(
            [new HtmlFragmentAdapter()],
            roles,
            ("use_chat_html", true)
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(
            Event("<b>hi</b>", isSubscriber: true)
        );

        result.Fragments.Should().ContainSingle().Which.Type.Should().Be("html");
        await roles
            .DidNotReceive()
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_badge_less_viewer_holding_the_link_capability_gets_a_link_preview()
    {
        IRoleResolver roles = Roles(html: false, links: true);
        ILinkPreviewService previews = Substitute.For<ILinkPreviewService>();
        previews
            .FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<LinkPreview?>(new("example.com", "Example", null, null)));
        ChatMessageDecorator decorator = Decorator(
            [new ExplodeTextAdapter(), new LinkPreviewAdapter(previews), new ImplodeTextAdapter()],
            roles,
            ("use_link_preview", true)
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(
            Event("check https://example.com out")
        );

        ChatMessageFragment link = result
            .Fragments.Should()
            .ContainSingle(f => f.Type == "link")
            .Which;
        link.LinkUrl.Should().Be("https://example.com");
        link.LinkPreview!.Title.Should().Be("Example");
        // Only the step the message can trigger is resolved — no HTML in the message, so no HTML lookup.
        await roles
            .DidNotReceive()
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                ChatDecorationCapabilities.RenderHtml,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_badge_less_viewer_with_no_link_grant_triggers_no_preview_fetch()
    {
        IRoleResolver roles = Roles(html: false, links: false);
        ILinkPreviewService previews = Substitute.For<ILinkPreviewService>();
        ChatMessageDecorator decorator = Decorator(
            [new ExplodeTextAdapter(), new LinkPreviewAdapter(previews), new ImplodeTextAdapter()],
            roles,
            ("use_link_preview", true)
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(
            Event("check https://example.com out")
        );

        result.Fragments.Should().ContainSingle().Which.Type.Should().Be("text");
        await previews.DidNotReceive().FetchAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_resolver_fails_closed_to_no_html()
    {
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Result<bool>>(_ => throw new InvalidOperationException("db down"));
        ChatMessageDecorator decorator = Decorator(
            [new HtmlFragmentAdapter()],
            roles,
            ("use_chat_html", true)
        );

        DecoratedChatMessage result = await decorator.DecorateAsync(Event("<b>hi</b>"));

        result.Fragments.Should().ContainSingle().Which.Type.Should().Be("text");
    }

    private static IRoleResolver Roles(bool html, bool links)
    {
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .HasCapabilityAsync(
                ViewerUser,
                Channel,
                ChatDecorationCapabilities.RenderHtml,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(html));
        roles
            .HasCapabilityAsync(
                ViewerUser,
                Channel,
                ChatDecorationCapabilities.PreviewLinks,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(links));
        return roles;
    }

    private static ChatMessageDecorator Decorator(
        IEnumerable<IChatDecorationAdapter> adapters,
        IRoleResolver roles,
        params (string Key, bool Enabled)[] toggles
    )
    {
        IFeatureService features = Substitute.For<IFeatureService>();
        List<FeatureStatusDto> dtos =
        [
            .. toggles.Select(toggle => new FeatureStatusDto(
                toggle.Key,
                toggle.Key,
                string.Empty,
                toggle.Enabled,
                null,
                []
            )),
        ];
        features
            .GetFeaturesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(dtos)));

        DateTime now = DateTime.UtcNow;
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                TwitchUserId,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(ViewerUser.ToString(), "viewer", "Viewer", null, null, now, now)
                )
            );

        ICacheService cache = Substitute.For<ICacheService>();
        cache
            .GetAsync<HashSet<string>>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HashSet<string>?>(null));

        return new(
            adapters,
            features,
            cache,
            users,
            roles,
            NullLogger<ChatMessageDecorator>.Instance
        );
    }

    private static ChatMessageReceivedEvent Event(string text, bool isSubscriber = false) =>
        new()
        {
            BroadcasterId = Channel,
            MessageId = "m1",
            TwitchBroadcasterId = "123",
            UserId = TwitchUserId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = text,
            Fragments = [new() { Type = "text", Text = text }],
            Badges = [],
            IsSubscriber = isSubscriber,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };
}
