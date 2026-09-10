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
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves S-RETRACT-a: when a real moderation domain event fires (message deleted, ban, timeout,
/// network nuke), <see cref="OverlayModerationRetractionHandler"/> publishes
/// <see cref="OverlayContentRetractedEvent"/> with the resolved local author/moderator ids and the
/// right <c>Reason</c>, AND pushes <see cref="IOverlayRetractionNotifier.RetractAsync"/> with the
/// platform-native author id a rendered widget can actually match — a state-change/side-effect proof,
/// not a "didn't throw" smoke test.
/// </summary>
public sealed class OverlayModerationRetractionHandlerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192c000-0000-7000-8000-0000000000e1");
    private const string AuthorTwitchId = "111222333";
    private const string ModeratorTwitchId = "444555666";

    private readonly IOverlayRetractionNotifier _overlay =
        Substitute.For<IOverlayRetractionNotifier>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();

    private static async Task SeedUserAsync(
        WidgetSqliteTestDatabase database,
        string twitchUserId,
        Guid localId
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Users.Add(
            new User
            {
                Id = localId,
                TwitchUserId = twitchUserId,
                Username = "viewer",
                UsernameNormalized = "viewer",
                DisplayName = "Viewer",
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Message_deleted_publishes_retraction_and_pushes_the_overlay_with_resolved_ids()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid authorLocalId = Guid.CreateVersion7();
        Guid moderatorLocalId = Guid.CreateVersion7();
        await SeedUserAsync(database, AuthorTwitchId, authorLocalId);
        await SeedUserAsync(database, ModeratorTwitchId, moderatorLocalId);

        using WidgetTestDbContext db = database.NewContext();
        OverlayModerationRetractionHandler handler = new(db, _overlay, _eventBus);

        await handler.HandleAsync(
            new ChatMessageDeletedEvent
            {
                BroadcasterId = Broadcaster,
                MessageId = "msg-42",
                DeletedByUserId = ModeratorTwitchId,
                TargetUserId = AuthorTwitchId,
            }
        );

        await _eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<OverlayContentRetractedEvent>(e =>
                    e.BroadcasterId == Broadcaster
                    && e.SourceMessageId == "msg-42"
                    && e.AuthorUserId == authorLocalId
                    && e.RetractedByUserId == moderatorLocalId
                    && e.Reason == "message_deleted"
                ),
                Arg.Any<CancellationToken>()
            );

        await _overlay
            .Received(1)
            .RetractAsync(
                Broadcaster,
                "msg-42",
                AuthorTwitchId,
                "message_deleted",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task User_banned_publishes_retraction_with_user_ban_reason_and_no_source_message()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid authorLocalId = Guid.CreateVersion7();
        await SeedUserAsync(database, AuthorTwitchId, authorLocalId);

        using WidgetTestDbContext db = database.NewContext();
        OverlayModerationRetractionHandler handler = new(db, _overlay, _eventBus);

        await handler.HandleAsync(
            new UserBannedEvent
            {
                BroadcasterId = Broadcaster,
                TargetUserId = AuthorTwitchId,
                TargetDisplayName = "BadActor",
                ModeratorUserId = ModeratorTwitchId,
            }
        );

        await _eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<OverlayContentRetractedEvent>(e =>
                    e.BroadcasterId == Broadcaster
                    && e.SourceMessageId == null
                    && e.AuthorUserId == authorLocalId
                    && e.Reason == "user_ban"
                ),
                Arg.Any<CancellationToken>()
            );

        await _overlay
            .Received(1)
            .RetractAsync(
                Broadcaster,
                null,
                AuthorTwitchId,
                "user_ban",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task User_timed_out_publishes_retraction_with_user_timeout_reason()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        using WidgetTestDbContext db = database.NewContext();
        OverlayModerationRetractionHandler handler = new(db, _overlay, _eventBus);

        await handler.HandleAsync(
            new UserTimedOutEvent
            {
                BroadcasterId = Broadcaster,
                TargetUserId = AuthorTwitchId,
                TargetDisplayName = "Chatty",
                ModeratorUserId = ModeratorTwitchId,
                DurationSeconds = 600,
            }
        );

        await _eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<OverlayContentRetractedEvent>(e =>
                    e.BroadcasterId == Broadcaster && e.Reason == "user_timeout"
                ),
                Arg.Any<CancellationToken>()
            );

        await _overlay
            .Received(1)
            .RetractAsync(
                Broadcaster,
                null,
                AuthorTwitchId,
                "user_timeout",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Network_nuke_executed_publishes_retraction_with_mod_retract_reason_from_origin_channel()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        using WidgetTestDbContext db = database.NewContext();
        OverlayModerationRetractionHandler handler = new(db, _overlay, _eventBus);
        Guid initiator = Guid.CreateVersion7();

        await handler.HandleAsync(
            new NetworkNukeExecutedEvent
            {
                BroadcasterId = Broadcaster,
                BatchId = Guid.CreateVersion7(),
                OriginBroadcasterId = Broadcaster,
                InitiatedByUserId = initiator,
                TargetTwitchUserId = AuthorTwitchId,
                ChannelCount = 5,
            }
        );

        await _eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<OverlayContentRetractedEvent>(e =>
                    e.BroadcasterId == Broadcaster
                    && e.Reason == "mod_retract"
                    && e.RetractedByUserId == initiator
                ),
                Arg.Any<CancellationToken>()
            );

        await _overlay
            .Received(1)
            .RetractAsync(
                Broadcaster,
                null,
                AuthorTwitchId,
                "mod_retract",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Retracting_an_author_with_no_local_user_row_is_a_no_op_never_an_error()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        using WidgetTestDbContext db = database.NewContext();
        OverlayModerationRetractionHandler handler = new(db, _overlay, _eventBus);

        // No User seeded anywhere for AuthorTwitchId — the platform id resolves to no local row.
        Func<Task> act = () =>
            handler.HandleAsync(
                new ChatMessageDeletedEvent
                {
                    BroadcasterId = Broadcaster,
                    MessageId = "msg-gone",
                    DeletedByUserId = ModeratorTwitchId,
                    TargetUserId = AuthorTwitchId,
                }
            );

        await act.Should().NotThrowAsync();

        await _eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<OverlayContentRetractedEvent>(e =>
                    e.AuthorUserId == null && e.RetractedByUserId == null
                ),
                Arg.Any<CancellationToken>()
            );

        // The overlay push still carries the platform id, so a widget can still match its own content.
        await _overlay
            .Received(1)
            .RetractAsync(
                Broadcaster,
                "msg-gone",
                AuthorTwitchId,
                "message_deleted",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Platform_level_event_with_no_broadcaster_never_publishes_or_pushes()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        using WidgetTestDbContext db = database.NewContext();
        OverlayModerationRetractionHandler handler = new(db, _overlay, _eventBus);

        await handler.HandleAsync(
            new ChatMessageDeletedEvent
            {
                BroadcasterId = Guid.Empty,
                MessageId = "msg-orphan",
                DeletedByUserId = ModeratorTwitchId,
                TargetUserId = AuthorTwitchId,
            }
        );

        await _eventBus
            .DidNotReceive()
            .PublishAsync(Arg.Any<OverlayContentRetractedEvent>(), Arg.Any<CancellationToken>());
        await _overlay
            .DidNotReceive()
            .RetractAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
