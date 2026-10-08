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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using NomNomzBot.Infrastructure.Tests.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the chat-filter execution path (moderation.md §3.2/§3.11): the handler runs a channel's enabled
/// <c>ChatFilter</c> rules against every incoming message and follows through — an <c>escalate</c> rule records
/// one ladder offense and applies the ladder's decision; a <c>timeout</c> rule times the sender out for its
/// configured duration; a sender at/above the filter's exemption floor is never touched; and a message that
/// matches nothing produces no action at all.
/// </summary>
public sealed class ChatFilterExecutionHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid SubjectUserId = Guid.Parse("0192a000-0000-7000-8000-0000000000aa");
    private const string TargetTwitchUserId = "viewer-123";
    private static readonly DateTimeOffset T0 = new(2026, 7, 17, 7, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ChatFilterExecutionHandler Handler,
        EventStoreTestDbContext Db,
        ITwitchModerationApi Moderation,
        ModerationEscalationService Escalation,
        SqliteTestDatabase Database,
        RecordingEventBus Bus
    )
    {
        /// <summary>
        /// A FRESH context on the same underlying database, for verification reads. <see cref="Db"/> stays
        /// tracking whatever it inserted across HandleAsync calls — ModerationEscalationService's cold-start
        /// insert path tracks the new row it adds, and EF's identity map then serves that stale in-memory
        /// instance back to any LATER same-context entity query instead of the fresh row a bypassing
        /// ExecuteUpdateAsync wrote (the same staleness AppendAsync's own comments document elsewhere).
        /// Reading through a separate context sidesteps that identity-map artifact entirely.
        /// </summary>
        public EventStoreTestDbContext Verify() => Database.NewContext();
    }

    private static Harness Build()
    {
        // A real relational SQLite context (S005/F13: ModerationEscalationService's atomic ExecuteUpdateAsync
        // increment is not supported at all by EF's InMemory provider, which this harness used before). The
        // SqliteTestDatabase's keep-alive connection is intentionally never disposed here — same lifetime
        // convention this file already used for its (also never-disposed) InMemory context per test.
        SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        moderation
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(AppliedBan()));
        moderation
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(AppliedBan()));
        moderation
            .WarnChatUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(new TwitchWarningResult("tw-chan", TargetTwitchUserId, "mod", "r"))
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
                        SubjectUserId.ToString(),
                        "viewer",
                        "Viewer",
                        null,
                        null,
                        default,
                        default
                    )
                )
            );

        ModerationEscalationService escalation = new(db, new FakeTimeProvider(T0));
        ModerationQueueService queue = new(
            db,
            users,
            moderation,
            Substitute.For<IModerationService>(),
            new FakeTimeProvider(T0),
            NullLogger<ModerationQueueService>.Instance
        );
        RecordingEventBus bus = new();
        ChatFilterExecutionHandler handler = new(
            db,
            moderation,
            escalation,
            queue,
            users,
            TestSanction.Held(),
            bus,
            NullLogger<ChatFilterExecutionHandler>.Instance
        );
        return new(handler, db, moderation, escalation, database, bus);
    }

    private static TwitchBanResult AppliedBan() =>
        new("tw-chan", "mod", TargetTwitchUserId, T0, null);

    private static ChatMessageReceivedEvent Message(string text, bool isVip = false) =>
        new()
        {
            MessageId = "msg-1",
            BroadcasterId = Channel,
            TwitchBroadcasterId = "tw-chan",
            UserId = TargetTwitchUserId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = text,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = isVip,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static async Task<ChatFilter> SeedBlocklistFilter(
        EventStoreTestDbContext db,
        ChatFilterAction action,
        List<string> terms,
        int? timeoutSeconds = null,
        int exemptMinRoleLevel = 10
    )
    {
        ChatFilter filter = new()
        {
            BroadcasterId = Channel,
            FilterType = ChatFilterType.Blocklist,
            Name = "test-filter",
            Action = action,
            TermsJson = System.Text.Json.JsonSerializer.Serialize(terms),
            TimeoutSeconds = timeoutSeconds,
            ExemptMinRoleLevel = exemptMinRoleLevel,
            IsEnabled = true,
        };
        db.ChatFilters.Add(filter);
        await db.SaveChangesAsync();
        return filter;
    }

    [Fact]
    public async Task An_escalate_rule_records_an_offense_and_applies_the_ladder_decision()
    {
        Harness h = Build();
        await h.Escalation.UpsertPolicyAsync(
            Channel,
            new(
                IsEnabled: true,
                Ladder: [new(1, "warn", null), new(2, "timeout", 60), new(3, "ban", null)],
                OffenseWindowHours: 168,
                CountAutoModViolations: false
            )
        );
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"]);

        await h.Handler.HandleAsync(Message("this is banned content"));

        // The offense was recorded on the ladder (J.11) for the resolved subject.
        ModerationEscalationState state = await h.Verify().ModerationEscalationStates.SingleAsync();
        state.SubjectUserId.Should().Be(SubjectUserId);
        state.OffenseCount.Should().Be(1);

        // First offense → the ladder returns "warn", which is applied via the moderation API.
        await h
            .Moderation.Received(1)
            .WarnChatUserAsync(
                Channel,
                TargetTwitchUserId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await h
            .Moderation.DidNotReceive()
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await h
            .Moderation.DidNotReceive()
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );

        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task The_second_escalate_offense_climbs_to_the_next_rung_and_times_out()
    {
        Harness h = Build();
        await h.Escalation.UpsertPolicyAsync(
            Channel,
            new(
                IsEnabled: true,
                Ladder: [new(1, "warn", null), new(2, "timeout", 60)],
                OffenseWindowHours: 168,
                CountAutoModViolations: false
            )
        );
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"]);

        await h.Handler.HandleAsync(Message("banned once"));
        await h.Handler.HandleAsync(Message("banned twice"));

        (await h.Verify().ModerationEscalationStates.SingleAsync()).OffenseCount.Should().Be(2);
        // Second offense → the ladder returns "timeout 60", applied with the ladder's duration (not the filter's).
        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                60,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_escalate_rule_with_the_ladder_on_deletes_the_message_and_applies_the_ladder_step()
    {
        Harness h = Build();
        h.Moderation.DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        await h.Escalation.UpsertPolicyAsync(
            Channel,
            new(
                IsEnabled: true,
                Ladder: [new(1, "timeout", 45), new(2, "ban", null)],
                OffenseWindowHours: 168,
                CountAutoModViolations: false
            )
        );
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"], timeoutSeconds: 999);

        await h.Handler.HandleAsync(Message("this is banned content"));

        await h
            .Moderation.Received(1)
            .DeleteChatMessageAsync(Channel, "msg-1", Arg.Any<CancellationToken>());
        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                45,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Verify().ModerationEscalationStates.SingleAsync()).OffenseCount.Should().Be(1);
    }

    [Fact]
    public async Task An_escalate_rule_with_the_ladder_off_deletes_the_message_and_times_out_for_the_filter_duration()
    {
        Harness h = Build();
        h.Moderation.DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"], timeoutSeconds: 120);

        await h.Handler.HandleAsync(Message("this is banned content"));

        await h
            .Moderation.Received(1)
            .DeleteChatMessageAsync(Channel, "msg-1", Arg.Any<CancellationToken>());
        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                120,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Verify().ModerationEscalationStates.CountAsync()).Should().Be(0);
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task An_escalate_rule_still_times_out_when_the_platform_delete_fails()
    {
        Harness h = Build();
        h.Moderation.DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("Twitch rejected it.", "TWITCH_ERROR"));
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"], timeoutSeconds: 120);

        await h.Handler.HandleAsync(Message("this is banned content"));

        await h
            .Moderation.Received(1)
            .DeleteChatMessageAsync(Channel, "msg-1", Arg.Any<CancellationToken>());
        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                120,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_timeout_rule_times_the_sender_out_for_the_configured_duration()
    {
        Harness h = Build();
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Timeout, ["spam"], timeoutSeconds: 300);

        await h.Handler.HandleAsync(Message("buy cheap spam now"));

        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                300,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task An_exempt_sender_is_left_untouched()
    {
        Harness h = Build();
        // Exempt at the VIP rung (level 4); the sender is a VIP, so the filter must skip them.
        await SeedBlocklistFilter(
            h.Db,
            ChatFilterAction.Timeout,
            ["spam"],
            timeoutSeconds: 300,
            exemptMinRoleLevel: 4
        );

        await h.Handler.HandleAsync(Message("spam spam spam", isVip: true));

        await h
            .Moderation.DidNotReceive()
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
    }

    [Fact]
    public async Task A_non_matching_message_produces_no_action()
    {
        Harness h = Build();
        await SeedBlocklistFilter(
            h.Db,
            ChatFilterAction.Timeout,
            ["forbidden"],
            timeoutSeconds: 300
        );

        await h.Handler.HandleAsync(Message("hello everyone, lovely stream"));

        await h
            .Moderation.DidNotReceive()
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await h
            .Moderation.DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
        (await h.Verify().ModerationEscalationStates.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_hold_rule_deletes_the_message_and_queues_it_for_review()
    {
        Harness h = Build();
        h.Moderation.DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Hold, ["sketchy"]);

        await h.Handler.HandleAsync(Message("a sketchy offer for you"));

        await h
            .Moderation.Received(1)
            .DeleteChatMessageAsync(Channel, "msg-1", Arg.Any<CancellationToken>());
        ModerationQueueItem row = await h.Verify().ModerationQueueItems.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.Source.Should().Be(ModerationQueueSource.ChatFilter);
        row.Status.Should().Be(ModerationQueueStatus.Pending);
        row.MessageContentSnapshot.Should().Be("a sketchy offer for you");
        row.TargetTwitchUserId.Should().Be(TargetTwitchUserId);
        row.TargetUsernameSnapshot.Should().Be("viewer");
        row.TargetUserId.Should().Be(SubjectUserId);
        row.AutoModMessageId.Should().Be("msg-1");
        row.AutoModCategory.Should().Be("test-filter");
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_hold_rule_still_queues_the_message_when_the_platform_delete_fails()
    {
        Harness h = Build();
        h.Moderation.DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("Twitch rejected it.", "TWITCH_ERROR"));
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Hold, ["sketchy"]);

        await h.Handler.HandleAsync(Message("a sketchy offer for you"));

        ModerationQueueItem row = await h.Verify().ModerationQueueItems.SingleAsync();
        row.Source.Should().Be(ModerationQueueSource.ChatFilter);
        row.Status.Should().Be(ModerationQueueStatus.Pending);
    }

    [Fact]
    public async Task A_flag_rule_queues_the_message_without_deleting_it()
    {
        Harness h = Build();
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Flag, ["sketchy"]);

        await h.Handler.HandleAsync(Message("a sketchy offer for you"));

        await h
            .Moderation.DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        ModerationQueueItem row = await h.Verify().ModerationQueueItems.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.Source.Should().Be(ModerationQueueSource.ChatFilter);
        row.Status.Should().Be(ModerationQueueStatus.Pending);
        row.MessageContentSnapshot.Should().Be("a sketchy offer for you");
        row.TargetTwitchUserId.Should().Be(TargetTwitchUserId);
        row.TargetUsernameSnapshot.Should().Be("viewer");
        row.AutoModCategory.Should().Be("test-filter");
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_refused_timeout_is_published_for_the_inbox_and_is_not_counted()
    {
        Harness h = Build();
        h.Moderation.TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchBanResult>("Not a moderator.", "TWITCH_FORBIDDEN"));
        ChatFilter filter = await SeedBlocklistFilter(
            h.Db,
            ChatFilterAction.Timeout,
            ["spam"],
            timeoutSeconds: 300
        );

        await h.Handler.HandleAsync(Message("buy cheap spam now"));

        ChatFilterActionFailedEvent failed = h
            .Bus.Published.Should()
            .ContainSingle()
            .Subject.Should()
            .BeOfType<ChatFilterActionFailedEvent>()
            .Subject;
        failed.BroadcasterId.Should().Be(Channel);
        failed.FilterId.Should().Be(filter.Id);
        failed.FilterName.Should().Be("test-filter");
        failed.Action.Should().Be("timeout");
        failed.SubjectTwitchUserId.Should().Be(TargetTwitchUserId);
        failed.SubjectUsername.Should().Be("viewer");
        failed.Error.Should().Be("Not a moderator.");
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
    }

    [Fact]
    public async Task A_refused_delete_is_published_for_the_inbox_and_is_not_counted()
    {
        Harness h = Build();
        h.Moderation.DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("Message is gone.", "TWITCH_NOT_FOUND"));
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Delete, ["spam"]);

        await h.Handler.HandleAsync(Message("buy cheap spam now"));

        ChatFilterActionFailedEvent failed = h
            .Bus.Published.Should()
            .ContainSingle()
            .Subject.Should()
            .BeOfType<ChatFilterActionFailedEvent>()
            .Subject;
        failed.Action.Should().Be("delete");
        failed.Error.Should().Be("Message is gone.");
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
    }

    [Fact]
    public async Task A_successful_timeout_publishes_nothing_and_counts_the_match()
    {
        Harness h = Build();
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Timeout, ["spam"], timeoutSeconds: 300);

        await h.Handler.HandleAsync(Message("buy cheap spam now"));

        h.Bus.Published.Should().BeEmpty();
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_refused_ladder_ban_is_published_with_the_ban_action()
    {
        Harness h = Build();
        h.Moderation.BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchBanResult>("Cannot ban a moderator.", "TWITCH_FORBIDDEN")
            );
        await h.Escalation.UpsertPolicyAsync(
            Channel,
            new(
                IsEnabled: true,
                Ladder: [new(1, "ban", null)],
                OffenseWindowHours: 168,
                CountAutoModViolations: false
            )
        );
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"]);

        await h.Handler.HandleAsync(Message("this is banned content"));

        ChatFilterActionFailedEvent failed = h
            .Bus.Published.Should()
            .ContainSingle()
            .Subject.Should()
            .BeOfType<ChatFilterActionFailedEvent>()
            .Subject;
        failed.Action.Should().Be("ban");
        failed.Error.Should().Be("Cannot ban a moderator.");
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
    }

    [Fact]
    public async Task A_refused_delete_in_an_escalate_rule_is_published_even_though_the_timeout_follows()
    {
        Harness h = Build();
        h.Moderation.DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("Message is gone.", "TWITCH_NOT_FOUND"));
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"], timeoutSeconds: 60);

        await h.Handler.HandleAsync(Message("this is banned content"));

        h.Bus.Published.OfType<ChatFilterActionFailedEvent>()
            .Should()
            .ContainSingle()
            .Which.Action.Should()
            .Be("delete");
        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                60,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
