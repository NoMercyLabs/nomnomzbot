// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A viewer reads a channel the bot does not serve, on the viewer's own token: the relay subscribes the three
/// chat topics with the viewer as user_id, pushes each line to that viewer's connection only, and closes the
/// viewer's session when the last channel is left or the connection drops.
/// </summary>
public sealed class ExternalChatRelayTests
{
    private const string ViewerTwitchId = "viewer-tw-1";
    private const string Channel = "off-bot-7";

    private const string ChatMessageEvent = """
        {"broadcaster_user_id":"off-bot-7","broadcaster_user_login":"offbot","chatter_user_id":"55",
         "chatter_user_login":"someone","chatter_user_name":"Someone","message_id":"m-1","color":"#FF0000",
         "message":{"text":"hello world","fragments":[{"type":"text","text":"hello world"}]},
         "badges":[{"set_id":"subscriber","id":"12","info":"12"}],
         "reply":{"parent_message_id":"m-0"}}
        """;

    private readonly Guid _viewer = Guid.NewGuid();
    private readonly FakeSessions _sessions = new();
    private readonly CapturingPush _push = new();
    private readonly IChatBadgeResolver _badges = EchoBadges();

    // Unless a test names the urls, a badge resolves to itself with no images (an un-warmed set).
    private static IChatBadgeResolver EchoBadges()
    {
        IChatBadgeResolver badges = Substitute.For<IChatBadgeResolver>();
        badges
            .ResolveByTwitchIdAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ChatBadge>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult<IReadOnlyList<ResolvedChatBadge>>([
                    .. call.Arg<IReadOnlyList<ChatBadge>>()
                        .Select(b => new ResolvedChatBadge(
                            b.SetId,
                            b.Id,
                            b.Info,
                            new Dictionary<string, string>()
                        )),
                ])
            );
        return badges;
    }

    // Unless a test says otherwise the viewer's own connection holds user:read:chat.
    private ExternalChatRelay NewRelay(bool canReadChat = true)
    {
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchUserIdAsync(_viewer, Arg.Any<CancellationToken>())
            .Returns(ViewerTwitchId);
        ITwitchTokenResolver tokens = Substitute.For<ITwitchTokenResolver>();
        tokens
            .HasUserScopeAsync(_viewer, TwitchScopes.UserReadChat, Arg.Any<CancellationToken>())
            .Returns(canReadChat);
        IServiceScopeFactory scopes = new ServiceCollection()
            .AddScoped<ITwitchIdentityResolver>(_ => identity)
            .AddScoped<ITwitchTokenResolver>(_ => tokens)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new(_sessions, _push, _badges, scopes, NullLogger<ExternalChatRelay>.Instance);
    }

    [Fact]
    public async Task Join_subscribes_the_three_chat_topics_on_the_viewers_own_session()
    {
        ExternalChatRelay relay = NewRelay();

        Result joined = await relay.JoinAsync(_viewer, "conn-1", Channel);

        joined.IsSuccess.Should().BeTrue(joined.ErrorMessage);
        _sessions
            .Opened.Should()
            .ContainSingle()
            .Which.Should()
            .Be(EventSubOwnerKeys.Viewer(_viewer));
        _sessions
            .Subscriptions.Select(s => s.EventType)
            .Should()
            .BeEquivalentTo([
                "channel.chat.message",
                "channel.chat.message_delete",
                "channel.chat.clear",
            ]);
        foreach (CapturedSubscription sub in _sessions.Subscriptions)
        {
            sub.ViewerUserId.Should().Be(_viewer, "the viewer's own token creates it");
            sub.SessionId.Should().Be("viewer-session");
            sub.Version.Should().Be("1");
            sub.Condition.Should()
                .BeEquivalentTo(
                    new Dictionary<string, string>
                    {
                        ["broadcaster_user_id"] = Channel,
                        ["user_id"] = ViewerTwitchId,
                    }
                );
        }
    }

    [Fact]
    public async Task Join_fails_missing_scope_and_opens_no_session_when_the_viewer_cannot_read_chat()
    {
        ExternalChatRelay relay = NewRelay(canReadChat: false);

        Result joined = await relay.JoinAsync(_viewer, "conn-1", Channel);

        joined.IsFailure.Should().BeTrue();
        joined.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        joined.ErrorMessage.Should().Contain(TwitchScopes.UserReadChat);
        _sessions.Opened.Should().BeEmpty("no EventSub session opens without the scope");
        _sessions.Subscriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_chat_line_reaches_only_the_connection_that_joined_that_channel()
    {
        ExternalChatRelay relay = NewRelay();
        await relay.JoinAsync(_viewer, "conn-1", Channel);
        await relay.JoinAsync(_viewer, "conn-2", "another-channel");

        await _sessions.Sink!.OnNotificationAsync(
            "n-1",
            new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero),
            "channel.chat.message",
            "1",
            Channel,
            JsonDocument.Parse(ChatMessageEvent).RootElement,
            CancellationToken.None
        );

        (string connectionId, ExternalChatEvent line) = _push
            .Pushed.Should()
            .ContainSingle()
            .Subject;
        connectionId.Should().Be("conn-1");
        line.Kind.Should().Be(ExternalChatEventKind.Message);
        line.BroadcasterTwitchId.Should().Be(Channel);
        line.MessageId.Should().Be("m-1");
        line.ChatterTwitchId.Should().Be("55");
        line.ChatterLogin.Should().Be("someone");
        line.ChatterDisplayName.Should().Be("Someone");
        line.Color.Should().Be("#FF0000");
        line.Text.Should().Be("hello world");
        line.Fragments.Should().ContainSingle().Which.Text.Should().Be("hello world");
        line.Badges.Should().ContainSingle().Which.SetId.Should().Be("subscriber");
        line.ReplyParentMessageId.Should().Be("m-0");
        line.Timestamp.Should().Be(new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        _sessions.Opened.Should().ContainSingle("two connections of one viewer share one session");
    }

    [Fact]
    public async Task A_pushed_line_carries_its_channels_badge_images_and_its_emote_images()
    {
        _badges
            .ResolveByTwitchIdAsync(
                Channel,
                Arg.Any<IReadOnlyList<ChatBadge>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult<IReadOnlyList<ResolvedChatBadge>>([
                    new ResolvedChatBadge(
                        "subscriber",
                        "12",
                        "12",
                        new Dictionary<string, string> { ["1"] = "https://badge/offbot-sub/1" }
                    ),
                ])
            );
        ExternalChatRelay relay = NewRelay();
        await relay.JoinAsync(_viewer, "conn-1", Channel);
        const string withEmote = """
            {"chatter_user_id":"55","chatter_user_login":"someone","message_id":"m-2",
             "message":{"text":"hi Kappa","fragments":[{"type":"text","text":"hi "},
               {"type":"emote","text":"Kappa","emote":{"id":"25","emote_set_id":"0","owner_id":"0","format":["static"]}}]},
             "badges":[{"set_id":"subscriber","id":"12","info":"12"}]}
            """;

        await _sessions.Sink!.OnNotificationAsync(
            "n-2",
            DateTimeOffset.UnixEpoch,
            "channel.chat.message",
            "1",
            Channel,
            JsonDocument.Parse(withEmote).RootElement,
            CancellationToken.None
        );

        ExternalChatEvent line = _push.Pushed.Should().ContainSingle().Subject.Event;
        line.Badges.Should()
            .ContainSingle()
            .Which.Urls["1"]
            .Should()
            .Be("https://badge/offbot-sub/1");
        ChatEmote emote = line.Fragments[1].Emote!;
        emote.Id.Should().Be("25");
        emote.Code.Should().Be("Kappa");
        emote.Urls["1"].Should().Be("https://static-cdn.jtvnw.net/emoticons/v2/25/static/dark/1.0");
        line.Fragments[0].Emote.Should().BeNull();
    }

    [Fact]
    public async Task A_deleted_message_and_a_clear_reach_the_viewer_with_their_kind()
    {
        ExternalChatRelay relay = NewRelay();
        await relay.JoinAsync(_viewer, "conn-1", Channel);

        await _sessions.Sink!.OnNotificationAsync(
            "n-2",
            DateTimeOffset.UnixEpoch,
            "channel.chat.message_delete",
            "1",
            Channel,
            JsonDocument
                .Parse("""{"broadcaster_user_id":"off-bot-7","message_id":"m-1"}""")
                .RootElement,
            CancellationToken.None
        );
        await _sessions.Sink!.OnNotificationAsync(
            "n-3",
            DateTimeOffset.UnixEpoch,
            "channel.chat.clear",
            "1",
            Channel,
            JsonDocument.Parse("""{"broadcaster_user_id":"off-bot-7"}""").RootElement,
            CancellationToken.None
        );

        _push
            .Pushed.Select(p => (p.Event.Kind, p.Event.MessageId))
            .Should()
            .Equal(
                (ExternalChatEventKind.MessageDeleted, "m-1"),
                (ExternalChatEventKind.ChatCleared, null)
            );
    }

    [Fact]
    public async Task Leaving_the_last_channel_closes_the_viewer_session()
    {
        ExternalChatRelay relay = NewRelay();
        await relay.JoinAsync(_viewer, "conn-1", Channel);
        await relay.JoinAsync(_viewer, "conn-2", Channel);

        await relay.LeaveAsync("conn-1", Channel);
        _sessions.Closed.Should().BeEmpty("conn-2 still reads the channel");

        await relay.LeaveAsync("conn-2", Channel);
        _sessions
            .Closed.Should()
            .ContainSingle()
            .Which.Should()
            .Be(EventSubOwnerKeys.Viewer(_viewer));
    }

    [Fact]
    public async Task A_dropped_connection_closes_the_session_and_gets_no_more_lines()
    {
        ExternalChatRelay relay = NewRelay();
        await relay.JoinAsync(_viewer, "conn-1", Channel);
        IEventSubNotificationSink sink = _sessions.Sink!;

        await relay.DisconnectAsync("conn-1");
        await sink.OnNotificationAsync(
            "n-4",
            DateTimeOffset.UnixEpoch,
            "channel.chat.message",
            "1",
            Channel,
            JsonDocument.Parse(ChatMessageEvent).RootElement,
            CancellationToken.None
        );

        _sessions
            .Closed.Should()
            .ContainSingle()
            .Which.Should()
            .Be(EventSubOwnerKeys.Viewer(_viewer));
        _push.Pushed.Should().BeEmpty();
    }

    private sealed record CapturedSubscription(
        Guid ViewerUserId,
        string SessionId,
        string EventType,
        string Version,
        IReadOnlyDictionary<string, string> Condition
    );

    private sealed class FakeSessions : IViewerEventSubSessions
    {
        private readonly ConcurrentQueue<CapturedSubscription> _subscriptions = new();
        private readonly ConcurrentQueue<string> _opened = new();
        private readonly ConcurrentQueue<string> _closed = new();

        public IEventSubNotificationSink? Sink { get; private set; }
        public IReadOnlyList<CapturedSubscription> Subscriptions => [.. _subscriptions];
        public IReadOnlyList<string> Opened => [.. _opened];
        public IReadOnlyList<string> Closed => [.. _closed];

        public Task<Result<string>> OpenAsync(
            string ownerKey,
            IEventSubNotificationSink sink,
            CancellationToken ct = default
        )
        {
            Sink = sink;
            _opened.Enqueue(ownerKey);
            return Task.FromResult(Result.Success("viewer-session"));
        }

        public Task<Result<string>> SubscribeAsViewerAsync(
            Guid viewerUserId,
            string sessionId,
            string eventType,
            string version,
            IReadOnlyDictionary<string, string> condition,
            CancellationToken ct = default
        )
        {
            _subscriptions.Enqueue(new(viewerUserId, sessionId, eventType, version, condition));
            return Task.FromResult(Result.Success($"sub-{eventType}"));
        }

        public Task CloseAsync(string ownerKey)
        {
            _closed.Enqueue(ownerKey);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingPush : IExternalChatPush
    {
        private readonly ConcurrentQueue<(string ConnectionId, ExternalChatEvent Event)> _pushed =
            new();

        public IReadOnlyList<(string ConnectionId, ExternalChatEvent Event)> Pushed => [.. _pushed];

        public Task PushAsync(
            string connectionId,
            ExternalChatEvent chatEvent,
            CancellationToken ct = default
        )
        {
            _pushed.Enqueue((connectionId, chatEvent));
            return Task.CompletedTask;
        }
    }
}
