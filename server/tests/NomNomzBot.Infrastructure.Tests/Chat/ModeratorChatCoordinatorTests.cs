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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A moderator reads the chat of the channels they moderate, on their own token: the coordinator subscribes the
/// eight chat-read topics per channel with the moderator as user_id, forwards the frames to the bot pipeline,
/// holds one reader per channel, and reports why a channel is not read.
/// </summary>
public sealed class ModeratorChatCoordinatorTests
{
    private static readonly string[] ChatReadTopics =
    [
        "channel.chat.message",
        "channel.chat.notification",
        "channel.chat.message_delete",
        "channel.chat.clear",
        "channel.chat.clear_user_messages",
        "channel.chat_settings.update",
        "channel.chat.user_message_hold",
        "channel.chat.user_message_update",
    ];

    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly FakeSessions _sessions = new();
    private readonly CapturingPipeline _pipeline = new();
    private readonly Dictionary<Guid, Moderator> _moderators = [];
    private readonly Dictionary<string, Guid> _tenants = [];
    private readonly HashSet<string> _onboarded = [];
    private readonly IChannelService _channels = Substitute.For<IChannelService>();
    private readonly IUserService _users = Substitute.For<IUserService>();
    private readonly List<(string TwitchId, Guid OwnerUserId)> _ensured = [];

    private sealed class Moderator
    {
        public required string TwitchId { get; init; }
        public List<string> Channels { get; set; } = [];
        public bool HasScope { get; set; } = true;
        public string? ListErrorCode { get; set; }
    }

    private ModeratorChatCoordinator NewCoordinator()
    {
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        ITwitchTokenResolver tokens = Substitute.For<ITwitchTokenResolver>();
        IOperatorModeratedChannelResolver moderated =
            Substitute.For<IOperatorModeratedChannelResolver>();
        foreach ((Guid userId, Moderator mod) in _moderators)
        {
            identity
                .GetTwitchUserIdAsync(userId, Arg.Any<CancellationToken>())
                .Returns(mod.TwitchId);
            tokens
                .HasUserScopeAsync(userId, TwitchScopes.UserReadChat, Arg.Any<CancellationToken>())
                .Returns(_ => mod.HasScope);
            moderated
                .ResolveAsync(userId, Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(ListFor(mod)));
        }

        _users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Task.FromResult(Result.Success(OwnerOf(call.ArgAt<string>(0)))));
        _channels
            .EnsureModeratedTenantAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string twitchId = call.ArgAt<string>(0);
                _ensured.Add((twitchId, call.ArgAt<Guid>(3)));
                return Task.FromResult(Result.Success(TenantOf(twitchId)));
            });
        _channels
            .GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Result.Success(ChannelOf(call.ArgAt<string>(0)))));

        IServiceScopeFactory scopes = new ServiceCollection()
            .AddScoped(_ => identity)
            .AddScoped(_ => tokens)
            .AddScoped(_ => moderated)
            .AddScoped(_ => _users)
            .AddScoped(_ => _channels)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new(_sessions, _pipeline, scopes, NullLogger<ModeratorChatCoordinator>.Instance);
    }

    private static Result<IReadOnlyList<TwitchModeratedChannel>> ListFor(Moderator mod)
    {
        if (mod.ListErrorCode is not null)
            return Result.Failure<IReadOnlyList<TwitchModeratedChannel>>(
                "listing failed",
                mod.ListErrorCode
            );

        // The operator's own channel always comes first (IOperatorModeratedChannelResolver).
        List<TwitchModeratedChannel> list = [new(mod.TwitchId, "own", "Own")];
        list.AddRange(
            mod.Channels.Select(id => new TwitchModeratedChannel(id, $"login-{id}", $"Name {id}"))
        );
        return Result.Success<IReadOnlyList<TwitchModeratedChannel>>(list);
    }

    private static UserDto OwnerOf(string twitchId) =>
        new(
            Guid.Parse($"00000000-0000-0000-0000-{int.Parse(twitchId.Replace("ch-", "0")):D12}")
                .ToString(),
            $"login-{twitchId}",
            $"Name {twitchId}",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow
        );

    private Guid TenantOf(string twitchId)
    {
        if (!_tenants.TryGetValue(twitchId, out Guid tenant))
            _tenants[twitchId] = tenant = Guid.NewGuid();
        return tenant;
    }

    private ChannelDto ChannelOf(string tenantId)
    {
        string twitchId = _tenants.First(t => t.Value.ToString() == tenantId).Key;
        return new(
            tenantId,
            $"login-{twitchId}",
            $"Name {twitchId}",
            null,
            false,
            _onboarded.Contains(twitchId),
            null,
            null,
            null,
            null,
            "free",
            null,
            DateTime.UtcNow
        );
    }

    private Guid AddModerator(string twitchId, params string[] channels)
    {
        Guid userId = twitchId == "alice-tw" ? _alice : _bob;
        _moderators[userId] = new() { TwitchId = twitchId, Channels = [.. channels] };
        return userId;
    }

    private static Task Notify(IEventSubNotificationSink sink, string type, string channel) =>
        sink.OnNotificationAsync(
            "n-1",
            new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            type,
            "1",
            channel,
            JsonDocument.Parse("{}").RootElement,
            CancellationToken.None
        );

    [Fact]
    public async Task A_moderated_channel_gets_all_eight_chat_read_topics_with_the_moderator_as_user_id()
    {
        Guid alice = AddModerator("alice-tw", "ch-1");
        ModeratorChatCoordinator coordinator = NewCoordinator();

        await coordinator.SyncUserAsync(alice);

        _sessions
            .Opened.Should()
            .ContainSingle()
            .Which.Should()
            .Be(EventSubOwnerKeys.Moderator(alice));
        _sessions.Subscriptions.Select(s => s.EventType).Should().BeEquivalentTo(ChatReadTopics);
        foreach (CapturedSubscription sub in _sessions.Subscriptions)
        {
            sub.ViewerUserId.Should().Be(alice, "the moderator's own token creates it");
            sub.Version.Should().Be("1");
            sub.Condition.Should()
                .BeEquivalentTo(
                    new Dictionary<string, string>
                    {
                        ["broadcaster_user_id"] = "ch-1",
                        ["user_id"] = "alice-tw",
                    }
                );
        }

        _ensured.Should().ContainSingle().Which.TwitchId.Should().Be("ch-1");
        coordinator
            .Snapshot()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new ModeratorChatReaderStatus("ch-1", ModeratorChatReaderState.Reading, alice, null)
            );
    }

    [Fact]
    public async Task A_frame_of_a_held_channel_reaches_the_pipeline_and_a_frame_of_another_channel_does_not()
    {
        Guid alice = AddModerator("alice-tw", "ch-1");
        ModeratorChatCoordinator coordinator = NewCoordinator();
        await coordinator.SyncUserAsync(alice);

        await Notify(
            _sessions.SinkFor(EventSubOwnerKeys.Moderator(alice)),
            "channel.chat.message",
            "ch-1"
        );
        await Notify(
            _sessions.SinkFor(EventSubOwnerKeys.Moderator(alice)),
            "channel.chat.message",
            "ch-9"
        );

        _pipeline
            .Notifications.Should()
            .ContainSingle()
            .Which.Should()
            .Be(("channel.chat.message", "ch-1"));
    }

    [Fact]
    public async Task Two_moderators_of_one_channel_share_one_reader_and_eight_subscriptions()
    {
        Guid alice = AddModerator("alice-tw", "ch-1");
        Guid bob = AddModerator("bob-tw", "ch-1");
        ModeratorChatCoordinator coordinator = NewCoordinator();

        await coordinator.SyncUserAsync(alice);
        await coordinator.SyncUserAsync(bob);

        _sessions
            .Opened.Should()
            .ContainSingle()
            .Which.Should()
            .Be(EventSubOwnerKeys.Moderator(alice));
        _sessions.Subscriptions.Should().HaveCount(8);
        coordinator
            .Snapshot()
            .Should()
            .ContainSingle(s => s.TwitchBroadcasterId == "ch-1")
            .Which.ModeratorUserId.Should()
            .Be(alice);
    }

    [Fact]
    public async Task A_channel_the_moderator_lost_is_released_marked_unmodded_and_its_session_closed()
    {
        Guid alice = AddModerator("alice-tw", "ch-1");
        ModeratorChatCoordinator coordinator = NewCoordinator();
        await coordinator.SyncUserAsync(alice);
        IEventSubNotificationSink sink = _sessions.SinkFor(EventSubOwnerKeys.Moderator(alice));

        _moderators[alice].Channels = [];
        await coordinator.SyncUserAsync(alice);
        await Notify(sink, "channel.chat.message", "ch-1");

        coordinator
            .Snapshot()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new ModeratorChatReaderStatus(
                    "ch-1",
                    ModeratorChatReaderState.Unmodded,
                    alice,
                    "Twitch no longer lists you as a moderator of this channel."
                )
            );
        _pipeline.Notifications.Should().BeEmpty("a released channel is no longer forwarded");
        _sessions
            .Closed.Should()
            .ContainSingle()
            .Which.Should()
            .Be(EventSubOwnerKeys.Moderator(alice));
    }

    [Fact]
    public async Task A_moderator_removed_revocation_drops_the_channel_and_sets_unmodded()
    {
        Guid alice = AddModerator("alice-tw", "ch-1", "ch-2");
        ModeratorChatCoordinator coordinator = NewCoordinator();
        await coordinator.SyncUserAsync(alice);
        IEventSubNotificationSink sink = _sessions.SinkFor(EventSubOwnerKeys.Moderator(alice));

        await sink.OnRevocationAsync(
            "sub-1",
            "channel.chat.message",
            "moderator_removed",
            "ch-1",
            CancellationToken.None
        );
        await Notify(sink, "channel.chat.message", "ch-1");
        await Notify(sink, "channel.chat.message", "ch-2");

        IReadOnlyList<ModeratorChatReaderStatus> snapshot = coordinator.Snapshot();
        snapshot
            .Single(s => s.TwitchBroadcasterId == "ch-1")
            .State.Should()
            .Be(ModeratorChatReaderState.Unmodded);
        snapshot
            .Single(s => s.TwitchBroadcasterId == "ch-2")
            .State.Should()
            .Be(ModeratorChatReaderState.Reading);
        _pipeline.Notifications.Should().ContainSingle().Which.Channel.Should().Be("ch-2");
    }

    [Fact]
    public async Task An_authorization_revoked_revocation_sets_no_token()
    {
        Guid alice = AddModerator("alice-tw", "ch-1");
        ModeratorChatCoordinator coordinator = NewCoordinator();
        await coordinator.SyncUserAsync(alice);

        await _sessions
            .SinkFor(EventSubOwnerKeys.Moderator(alice))
            .OnRevocationAsync(
                "sub-1",
                "channel.chat.message",
                "authorization_revoked",
                "ch-1",
                CancellationToken.None
            );

        coordinator
            .Snapshot()
            .Should()
            .ContainSingle()
            .Which.State.Should()
            .Be(ModeratorChatReaderState.NoToken);
    }

    [Fact]
    public async Task More_channels_than_one_session_can_carry_are_over_cap_with_a_reason()
    {
        string[] many = [.. Enumerable.Range(1, 38).Select(i => $"ch-{i}")];
        Guid alice = AddModerator("alice-tw", many);
        ModeratorChatCoordinator coordinator = NewCoordinator();

        await coordinator.SyncUserAsync(alice);

        IReadOnlyList<ModeratorChatReaderStatus> snapshot = coordinator.Snapshot();
        snapshot.Count(s => s.State == ModeratorChatReaderState.Reading).Should().Be(37);
        ModeratorChatReaderStatus over = snapshot.Single(s =>
            s.State == ModeratorChatReaderState.OverCap
        );
        over.TwitchBroadcasterId.Should().Be("ch-38");
        over.Reason.Should().Contain("37");
        _sessions
            .Subscriptions.Should()
            .HaveCount(37 * 8, "8 subscriptions per channel, 296 of 300");
    }

    [Fact]
    public async Task A_moderator_without_a_token_marks_the_held_channels_no_token_and_forwards_nothing()
    {
        Guid alice = AddModerator("alice-tw", "ch-1");
        ModeratorChatCoordinator coordinator = NewCoordinator();
        await coordinator.SyncUserAsync(alice);
        IEventSubNotificationSink sink = _sessions.SinkFor(EventSubOwnerKeys.Moderator(alice));

        _moderators[alice].ListErrorCode = TwitchErrorCodes.NoToken;
        await coordinator.SyncUserAsync(alice);
        await Notify(sink, "channel.chat.message", "ch-1");

        coordinator
            .Snapshot()
            .Should()
            .ContainSingle()
            .Which.State.Should()
            .Be(ModeratorChatReaderState.NoToken);
        _pipeline.Notifications.Should().BeEmpty();
        _sessions.Closed.Should().ContainSingle();
    }

    [Fact]
    public async Task A_moderator_without_the_chat_scope_gets_missing_scope_per_channel_and_no_session()
    {
        Guid alice = AddModerator("alice-tw", "ch-1", "ch-2");
        _moderators[alice].HasScope = false;
        ModeratorChatCoordinator coordinator = NewCoordinator();

        await coordinator.SyncUserAsync(alice);

        _sessions.Opened.Should().BeEmpty();
        _sessions.Subscriptions.Should().BeEmpty();
        coordinator
            .Snapshot()
            .Should()
            .OnlyContain(s =>
                s.State == ModeratorChatReaderState.MissingScope
                && s.Reason!.Contains(TwitchScopes.UserReadChat)
            )
            .And.HaveCount(2);
    }

    [Fact]
    public async Task The_moderators_own_channel_and_onboarded_channels_are_not_read()
    {
        Guid alice = AddModerator("alice-tw", "ch-1", "ch-2");
        _onboarded.Add("ch-2");
        ModeratorChatCoordinator coordinator = NewCoordinator();

        await coordinator.SyncUserAsync(alice);

        _sessions
            .Subscriptions.Select(s => s.Condition["broadcaster_user_id"])
            .Distinct()
            .Should()
            .BeEquivalentTo(["ch-1"]);
        coordinator
            .Snapshot()
            .Should()
            .ContainSingle()
            .Which.TwitchBroadcasterId.Should()
            .Be("ch-1");
        _ensured
            .Should()
            .NotContain(e => e.TwitchId == "alice-tw", "the own channel is never provisioned here");
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
        private readonly ConcurrentDictionary<string, IEventSubNotificationSink> _sinks = new();

        public IReadOnlyList<CapturedSubscription> Subscriptions => [.. _subscriptions];
        public IReadOnlyList<string> Opened => [.. _opened];
        public IReadOnlyList<string> Closed => [.. _closed];

        public IEventSubNotificationSink SinkFor(string ownerKey) => _sinks[ownerKey];

        public Task<Result<string>> OpenAsync(
            string ownerKey,
            IEventSubNotificationSink sink,
            CancellationToken ct = default
        )
        {
            _sinks[ownerKey] = sink;
            _opened.Enqueue(ownerKey);
            return Task.FromResult(Result.Success($"session-{ownerKey}"));
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
            _sinks.TryRemove(ownerKey, out _);
            _closed.Enqueue(ownerKey);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingPipeline : IEventSubNotificationSink
    {
        private readonly ConcurrentQueue<(string Type, string Channel)> _notifications = new();

        public IReadOnlyList<(string Type, string Channel)> Notifications => [.. _notifications];

        public Task OnNotificationAsync(
            string messageId,
            DateTimeOffset messageTimestamp,
            string subscriptionType,
            string subscriptionVersion,
            string twitchBroadcasterUserId,
            JsonElement @event,
            CancellationToken ct
        )
        {
            _notifications.Enqueue((subscriptionType, twitchBroadcasterUserId));
            return Task.CompletedTask;
        }

        public Task OnRevocationAsync(
            string twitchSubscriptionId,
            string subscriptionType,
            string status,
            string twitchBroadcasterUserId,
            CancellationToken ct
        ) => Task.CompletedTask;

        public Task OnSessionWelcomeAsync(
            string sessionId,
            string ownerKey,
            string? handoffFromSessionId,
            CancellationToken ct
        ) => Task.CompletedTask;

        public Task<bool> ShouldReconnectAsync(string ownerKey, CancellationToken ct) =>
            Task.FromResult(true);

        public Task OnSessionDisconnectedAsync(
            string ownerKey,
            string? sessionId,
            string reason,
            TimeSpan nextRetryIn,
            CancellationToken ct
        ) => Task.CompletedTask;
    }
}
