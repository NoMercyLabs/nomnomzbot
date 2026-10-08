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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves a chat filter acts on the platform the message came from: Twitch keeps its Helix path, and a Kick
/// or YouTube message is moderated through <see cref="IInboundOriginModerator"/> with the message's own
/// provider — never Helix, which would take a non-Twitch message id or user id for a Twitch one.
/// </summary>
public sealed class ChatFilterOriginPlatformTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private const string ViewerId = "viewer-123";
    private const string FilterReason = "Chat filter: test-filter";
    private static readonly DateTimeOffset T0 = new(2026, 7, 17, 7, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ChatFilterExecutionHandler Handler,
        EventStoreTestDbContext Db,
        ITwitchModerationApi Helix,
        IInboundOriginModerator Origin,
        IUserService Users,
        ModerationEscalationService Escalation,
        SqliteTestDatabase Database,
        RecordingEventBus Bus
    )
    {
        public EventStoreTestDbContext Verify() => Database.NewContext();
    }

    private static Harness Build()
    {
        SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();

        ITwitchModerationApi helix = Substitute.For<ITwitchModerationApi>();
        helix
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        helix
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new TwitchBanResult("tw-chan", "mod", ViewerId, T0, null)));

        IInboundOriginModerator origin = Substitute.For<IInboundOriginModerator>();
        origin
            .DeleteMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(InboundModerationOutcome.Done());
        origin
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(InboundModerationOutcome.Done());
        origin
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(InboundModerationOutcome.Done());

        IUserService users = Substitute.For<IUserService>();
        ModerationEscalationService escalation = new(db, new FakeTimeProvider(T0));
        ModerationQueueService queue = new(
            db,
            users,
            helix,
            Substitute.For<IModerationService>(),
            Substitute.For<NomNomzBot.Domain.Platform.Interfaces.IEventBus>(),
            new FakeTimeProvider(T0),
            NullLogger<ModerationQueueService>.Instance
        );
        RecordingEventBus bus = new();
        ChatFilterExecutionHandler handler = new(
            db,
            helix,
            origin,
            escalation,
            queue,
            users,
            TestSanction.Held(),
            bus,
            NullLogger<ChatFilterExecutionHandler>.Instance
        );
        return new(handler, db, helix, origin, users, escalation, database, bus);
    }

    private static ChatMessageReceivedEvent Message(string provider, string text) =>
        new()
        {
            MessageId = "msg-1",
            BroadcasterId = Channel,
            TwitchBroadcasterId = "tw-chan",
            Provider = provider,
            UserId = ViewerId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = text,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static async Task SeedFilter(
        EventStoreTestDbContext db,
        ChatFilterAction action,
        int? timeoutSeconds = null
    )
    {
        db.ChatFilters.Add(
            new()
            {
                BroadcasterId = Channel,
                FilterType = ChatFilterType.Blocklist,
                Name = "test-filter",
                Action = action,
                TermsJson = System.Text.Json.JsonSerializer.Serialize(new List<string> { "spam" }),
                TimeoutSeconds = timeoutSeconds,
                ExemptMinRoleLevel = 10,
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task AssertHelixUntouched(Harness h)
    {
        await h.Helix.DidNotReceiveWithAnyArgs().DeleteChatMessageAsync(default, default!);
        await h
            .Helix.DidNotReceiveWithAnyArgs()
            .TimeoutUserAsync(default, default!, default, default!);
        await h.Helix.DidNotReceiveWithAnyArgs().BanUserAsync(default, default!, default!);
        await h.Helix.DidNotReceiveWithAnyArgs().WarnChatUserAsync(default, default!, default!);
    }

    [Fact]
    public async Task A_youtube_message_on_a_delete_filter_is_deleted_through_the_seam_and_never_reaches_helix()
    {
        Harness h = Build();
        await SeedFilter(h.Db, ChatFilterAction.Delete);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.YouTube, "buy cheap spam now"));

        await h
            .Origin.Received(1)
            .DeleteMessageAsync(Channel, "youtube", "msg-1", Arg.Any<CancellationToken>());
        await AssertHelixUntouched(h);
        h.Bus.Published.Should().BeEmpty();
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_kick_message_on_a_timeout_filter_is_timed_out_through_the_seam_for_the_filters_seconds()
    {
        Harness h = Build();
        await SeedFilter(h.Db, ChatFilterAction.Timeout, timeoutSeconds: 300);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.Kick, "buy cheap spam now"));

        await h
            .Origin.Received(1)
            .TimeoutUserAsync(
                Channel,
                "kick",
                ViewerId,
                300,
                FilterReason,
                Arg.Any<CancellationToken>()
            );
        await h.Origin.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default!, default!);
        await AssertHelixUntouched(h);
        h.Bus.Published.Should().BeEmpty();
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_kick_timeout_filter_without_seconds_uses_the_default_of_600()
    {
        Harness h = Build();
        await SeedFilter(h.Db, ChatFilterAction.Timeout);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.Kick, "buy cheap spam now"));

        await h
            .Origin.Received(1)
            .TimeoutUserAsync(
                Channel,
                "kick",
                ViewerId,
                600,
                FilterReason,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_not_supported_delete_is_published_for_the_inbox_and_is_not_counted()
    {
        Harness h = Build();
        h.Origin.DeleteMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(InboundModerationOutcome.NotSupported("No platform for youtube."));
        await SeedFilter(h.Db, ChatFilterAction.Delete);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.YouTube, "buy cheap spam now"));

        ChatFilterActionFailedEvent failed = h
            .Bus.Published.Should()
            .ContainSingle()
            .Subject.Should()
            .BeOfType<ChatFilterActionFailedEvent>()
            .Subject;
        failed.BroadcasterId.Should().Be(Channel);
        failed.FilterName.Should().Be("test-filter");
        failed.Action.Should().Be("delete");
        failed.SubjectTwitchUserId.Should().Be(ViewerId);
        failed.SubjectUsername.Should().Be("viewer");
        failed.Error.Should().Be("No platform for youtube.");
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
        await AssertHelixUntouched(h);
    }

    [Fact]
    public async Task A_failed_timeout_is_published_with_the_platforms_reason_and_is_not_counted()
    {
        Harness h = Build();
        h.Origin.TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(InboundModerationOutcome.Failed("Kick refused the timeout."));
        await SeedFilter(h.Db, ChatFilterAction.Timeout, timeoutSeconds: 120);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.Kick, "buy cheap spam now"));

        ChatFilterActionFailedEvent failed = h
            .Bus.Published.Should()
            .ContainSingle()
            .Subject.Should()
            .BeOfType<ChatFilterActionFailedEvent>()
            .Subject;
        failed.Action.Should().Be("timeout");
        failed.Error.Should().Be("Kick refused the timeout.");
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
    }

    [Fact]
    public async Task A_twitch_message_still_goes_to_helix_only()
    {
        Harness h = Build();
        await SeedFilter(h.Db, ChatFilterAction.Delete);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.Twitch, "buy cheap spam now"));

        await h
            .Helix.Received(1)
            .DeleteChatMessageAsync(Channel, "msg-1", Arg.Any<CancellationToken>());
        await h.Origin.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default!, default!);
        await h
            .Origin.DidNotReceiveWithAnyArgs()
            .TimeoutUserAsync(default, default!, default!, default);
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_kick_escalate_rule_deletes_and_times_out_through_the_seam_and_skips_the_twitch_keyed_ladder()
    {
        Harness h = Build();
        await h.Escalation.UpsertPolicyAsync(
            Channel,
            new(
                IsEnabled: true,
                Ladder: [new(1, "ban", null)],
                OffenseWindowHours: 168,
                CountAutoModViolations: false
            )
        );
        await SeedFilter(h.Db, ChatFilterAction.Escalate, timeoutSeconds: 45);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.Kick, "buy cheap spam now"));

        await h
            .Origin.Received(1)
            .DeleteMessageAsync(Channel, "kick", "msg-1", Arg.Any<CancellationToken>());
        await h
            .Origin.Received(1)
            .TimeoutUserAsync(
                Channel,
                "kick",
                ViewerId,
                45,
                FilterReason,
                Arg.Any<CancellationToken>()
            );
        await h.Origin.DidNotReceiveWithAnyArgs().BanUserAsync(default, default!, default!);
        (await h.Verify().ModerationEscalationStates.CountAsync()).Should().Be(0);
        await h
            .Users.DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync(default!, default!, default!, default!);
        await AssertHelixUntouched(h);
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_kick_hold_rule_deletes_through_the_seam_and_does_not_queue_a_twitch_review_row()
    {
        Harness h = Build();
        await SeedFilter(h.Db, ChatFilterAction.Hold);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.Kick, "buy cheap spam now"));

        await h
            .Origin.Received(1)
            .DeleteMessageAsync(Channel, "kick", "msg-1", Arg.Any<CancellationToken>());
        (await h.Verify().ModerationQueueItems.CountAsync()).Should().Be(0);
        await AssertHelixUntouched(h);
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_kick_flag_rule_queues_nothing_and_is_not_counted()
    {
        Harness h = Build();
        await SeedFilter(h.Db, ChatFilterAction.Flag);

        await h.Handler.HandleAsync(Message(AuthEnums.Platform.Kick, "buy cheap spam now"));

        (await h.Verify().ModerationQueueItems.CountAsync()).Should().Be(0);
        await h.Origin.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default!, default!);
        await AssertHelixUntouched(h);
        (await h.Verify().ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
    }
}
