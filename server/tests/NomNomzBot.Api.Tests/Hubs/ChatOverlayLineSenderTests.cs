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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="ChatOverlayLineSender"/> puts a bot line on the chat widgets that are open right now, as an
/// <c>overlay_line</c> chat message under the bot identity, and puts it nowhere else: no widget that is closed,
/// not subscribed or disabled, no generic overlay feed, and never the dashboard chat feed.
/// </summary>
public sealed class ChatOverlayLineSenderTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _channel = Guid.CreateVersion7();
    private readonly IWidgetNotifier _widgets = Substitute.For<IWidgetNotifier>();
    private readonly IOverlayPresenceRegistry _presence =
        Substitute.For<IOverlayPresenceRegistry>();
    private readonly WidgetTestDbContext _db = WidgetTestDbContext.New();

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task An_open_chat_widget_gets_one_overlay_line_chat_message_under_the_bot_identity()
    {
        Widget chat = await AddWidgetAsync("Chat box", true, "ChatMessage");
        _presence.IsWidgetAttached(_channel, chat.Id).Returns(true);

        ChatOverlayDelivery delivery = await Sut()
            .SendAsync(Line("<marquee>CHOO CHOO Viewer</marquee>", isHtml: true));

        delivery.WidgetsReached.Should().Be(1);
        delivery.WasDelivered.Should().BeTrue();
        WidgetEventDto pushed = Single(Pushes());
        pushed.WidgetId.Should().Be(chat.Id.ToString());
        pushed.EventType.Should().Be("ChatMessage");
        DashboardChatMessageDto dto = pushed
            .Data.Should()
            .BeOfType<DashboardChatMessageDto>()
            .Subject;
        dto.MessageType.Should().Be("overlay_line");
        dto.UserType.Should().Be("bot");
        dto.UserId.Should().Be("bot-1");
        dto.DisplayName.Should().Be("NomNomzBot");
        dto.Username.Should().Be("NomNomzBot");
        dto.ChannelId.Should().Be(_channel.ToString());
        dto.Message.Should().Be("<marquee>CHOO CHOO Viewer</marquee>");
        dto.Timestamp.Should().Be(Now.ToString("O"));
        Guid.TryParse(dto.Id, out Guid id).Should().BeTrue();
        id.Should().NotBe(Guid.Empty);
        dto.IsCommand.Should().BeFalse();
        dto.IsBroadcaster.Should().BeFalse();
        dto.Badges.Should().BeEmpty();
        dto.Fragments.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new ChatFragmentDto(
                    "html",
                    "<marquee>CHOO CHOO Viewer</marquee>",
                    null,
                    null,
                    null,
                    null,
                    null
                )
            );
    }

    [Fact]
    public async Task A_plain_text_line_travels_as_a_text_fragment()
    {
        Widget chat = await AddWidgetAsync("Chat box", true, "ChatMessage");
        _presence.IsWidgetAttached(_channel, chat.Id).Returns(true);

        await Sut().SendAsync(Line("hello there", isHtml: false));

        DashboardChatMessageDto dto = (DashboardChatMessageDto)Single(Pushes()).Data!;
        dto.Fragments.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new ChatFragmentDto("text", "hello there", null, null, null, null, null)
            );
    }

    [Fact]
    public async Task Only_open_subscribed_enabled_widgets_get_the_line_and_the_count_says_so()
    {
        Widget open = await AddWidgetAsync("Chat open", true, "ChatMessage");
        Widget second = await AddWidgetAsync("Chat second", true, "ChatMessage");
        Widget closed = await AddWidgetAsync("Chat closed", true, "ChatMessage");
        Widget notSubscribed = await AddWidgetAsync("Follow alert", true, "follow");
        Widget disabled = await AddWidgetAsync("Chat disabled", false, "ChatMessage");
        foreach (Widget widget in new[] { open, second, notSubscribed, disabled })
            _presence.IsWidgetAttached(_channel, widget.Id).Returns(true);
        _presence.IsWidgetAttached(_channel, closed.Id).Returns(false);

        ChatOverlayDelivery delivery = await Sut().SendAsync(Line("hi", isHtml: false));

        delivery.WidgetsReached.Should().Be(2);
        List<string> reached = Pushes().Select(p => p.WidgetId).ToList();
        reached.Should().BeEquivalentTo([open.Id.ToString(), second.Id.ToString()]);
    }

    [Fact]
    public async Task No_open_chat_widget_pushes_nothing_anywhere_and_reports_no_overlay_open()
    {
        Widget closed = await AddWidgetAsync("Chat closed", true, "ChatMessage");
        _presence.IsWidgetAttached(_channel, closed.Id).Returns(false);

        ChatOverlayDelivery delivery = await Sut().SendAsync(Line("hi", isHtml: false));

        delivery.Should().Be(ChatOverlayDelivery.NoChatOverlayOpen);
        delivery.WasDelivered.Should().BeFalse();
        await _widgets
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default!, default!, default!, default);
        await _widgets
            .DidNotReceiveWithAnyArgs()
            .BroadcastOverlayEventAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_channel_with_no_widget_at_all_reports_no_overlay_open()
    {
        ChatOverlayDelivery delivery = await Sut().SendAsync(Line("hi", isHtml: false));

        delivery.Should().Be(ChatOverlayDelivery.NoChatOverlayOpen);
    }

    [Fact]
    public async Task A_widget_of_another_channel_never_receives_the_line()
    {
        Widget foreign = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Guid.CreateVersion7(),
            Name = "Other channel chat",
            IsEnabled = true,
            EventSubscriptions = ["ChatMessage"],
        };
        _db.Widgets.Add(foreign);
        await _db.SaveChangesAsync();
        _presence.IsWidgetAttached(Arg.Any<Guid>(), foreign.Id).Returns(true);

        ChatOverlayDelivery delivery = await Sut().SendAsync(Line("hi", isHtml: false));

        delivery.Should().Be(ChatOverlayDelivery.NoChatOverlayOpen);
        await _widgets
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task The_line_never_reaches_the_dashboard_chat_feed_or_the_generic_overlay_feed()
    {
        Widget chat = await AddWidgetAsync("Chat box", true, "ChatMessage");
        _presence.IsWidgetAttached(_channel, chat.Id).Returns(true);

        await Sut().SendAsync(Line("hi", isHtml: false));

        // The sender takes no IDashboardNotifier at all, so the dashboard chat feed is out of reach by
        // construction. The generic overlay feed would reach widgets that are not chat widgets, so it stays quiet too.
        typeof(ChatOverlayLineSender)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .Should()
            .NotContain(typeof(IDashboardNotifier));
        await _widgets
            .DidNotReceiveWithAnyArgs()
            .BroadcastOverlayEventAsync(default!, default!, default);
    }

    private ChatOverlayLineSender Sut() => new(_db, _widgets, _presence, new FakeTimeProvider(Now));

    [Fact]
    public async Task The_payload_names_the_requester_and_carries_the_look_without_changing_the_bot_identity()
    {
        Widget chat = await AddWidgetAsync("Chat box", true, "ChatMessage");
        _presence.IsWidgetAttached(_channel, chat.Id).Returns(true);
        ChatOverlayLine line = Line("<marquee>CHOO CHOO</marquee>", isHtml: true) with
        {
            RequestedBy = "Viewer One",
            Look = new ReplyLook(ReplyHighlight.Warning, ReplySize.Large, DurationSeconds: 45),
        };

        await Sut().SendAsync(line);

        DashboardChatMessageDto dto = (DashboardChatMessageDto)Single(Pushes()).Data!;
        dto.OverlayLine.Should()
            .Be(new OverlayLineDto("Viewer One", "warning", "large", DurationSeconds: 45));
        // The viewer is only the requester: the line stays the bot's, never the viewer's.
        dto.DisplayName.Should().Be("NomNomzBot");
        dto.UserType.Should().Be("bot");
        dto.UserId.Should().Be("bot-1");
        dto.Message.Should().NotContain("Viewer One");
    }

    [Fact]
    public async Task A_line_with_no_look_and_no_requester_carries_the_defaults()
    {
        Widget chat = await AddWidgetAsync("Chat box", true, "ChatMessage");
        _presence.IsWidgetAttached(_channel, chat.Id).Returns(true);

        await Sut().SendAsync(Line("hi", isHtml: false));

        DashboardChatMessageDto dto = (DashboardChatMessageDto)Single(Pushes()).Data!;
        dto.OverlayLine.Should().Be(new OverlayLineDto(null, null, "normal", null));
    }

    private ChatOverlayLine Line(string text, bool isHtml) =>
        new(_channel, "bot-1", "NomNomzBot", text, isHtml);

    private async Task<Widget> AddWidgetAsync(string name, bool enabled, string subscription)
    {
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = _channel,
            Name = name,
            IsEnabled = enabled,
            EventSubscriptions = [subscription],
        };
        _db.Widgets.Add(widget);
        await _db.SaveChangesAsync();
        return widget;
    }

    private List<WidgetEventDto> Pushes() =>
        _widgets
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IWidgetNotifier.SendWidgetEventAsync))
            .Select(c => (WidgetEventDto)c.GetArguments()[2]!)
            .ToList();

    private static WidgetEventDto Single(List<WidgetEventDto> pushes)
    {
        pushes.Should().ContainSingle();
        return pushes[0];
    }
}
