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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Notifications.Sources;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Pending Twitch unban requests (appeals) reach the moderators' attention inbox: one item per pending appeal,
/// read live from Twitch as the channel owner, never turned into an empty list when Twitch cannot be read, and
/// refreshed live when an appeal is filed or resolved on Twitch.
/// </summary>
public sealed class UnbanRequestSourceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-0000000000e2");
    private static readonly DateTime FiledAt = new(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc);

    private static (ActionRequiredInboxServiceTestDbContext Db, IModerationService Moderation) Seed(
        Result<List<UnbanRequestDto>> twitchAnswer
    )
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Owner,
                Name = "streamer",
                NameNormalized = "streamer",
                TwitchChannelId = "100",
            }
        );
        db.SaveChanges();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .GetUnbanRequestsAsync(
                Channel.ToString(),
                Owner,
                "pending",
                Arg.Any<CancellationToken>()
            )
            .Returns(twitchAnswer);
        return (db, moderation);
    }

    private static UnbanRequestDto Request(
        string id,
        string status = "pending",
        string userName = "BannedBob",
        string text = "I am sorry, please unban me"
    ) =>
        new(
            Id: id,
            UserId: "7001",
            UserLogin: userName.ToLowerInvariant(),
            UserName: userName,
            Text: text,
            Status: status,
            CreatedAt: FiledAt,
            ResolvedAt: null,
            ResolvedBy: null,
            ResolutionText: null
        );

    [Fact]
    public async Task A_pending_appeal_becomes_one_warning_item_with_the_contract_fields()
    {
        (ActionRequiredInboxServiceTestDbContext db, IModerationService moderation) = Seed(
            Result.Success<List<UnbanRequestDto>>([Request("req-1")])
        );

        Result<List<ActionRequiredItemDto>> items = await new UnbanRequestSource(
            db,
            moderation
        ).GetItemsAsync(Channel, new HashSet<string>());

        items.IsSuccess.Should().BeTrue(items.ErrorMessage);
        ActionRequiredItemDto item = items.Value.Should().ContainSingle().Subject;
        item.Id.Should().Be("unban:req-1");
        item.Kind.Should().Be("unban_request");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_unban_request_title");
        item.MessageKey.Should().Be("attention_unban_request_message");
        item.Parameters.Should()
            .BeEquivalentTo(
                new Dictionary<string, string>
                {
                    ["username"] = "BannedBob",
                    ["text"] = "I am sorry, please unban me",
                }
            );
        item.DeepLinkRoute.Should().Be("moderation");
        item.SourceUserId.Should().Be("7001");
        item.SourceUserName.Should().Be("BannedBob");
        item.Count.Should().Be(1);
        item.QueueItemIds.Should().BeEmpty();
        item.DetectedAt.Should().Be(FiledAt);
    }

    [Fact]
    public async Task Resolved_appeals_and_dismissed_keys_are_left_out()
    {
        (ActionRequiredInboxServiceTestDbContext db, IModerationService moderation) = Seed(
            Result.Success<List<UnbanRequestDto>>([
                Request("req-open"),
                Request("req-dismissed"),
                Request("req-done", status: "approved"),
            ])
        );

        Result<List<ActionRequiredItemDto>> items = await new UnbanRequestSource(
            db,
            moderation
        ).GetItemsAsync(Channel, new HashSet<string> { "unban:req-dismissed" });

        items.Value.Select(i => i.Id).Should().Equal("unban:req-open");
    }

    [Fact]
    public async Task A_failed_twitch_read_is_a_failure_not_an_empty_list()
    {
        (ActionRequiredInboxServiceTestDbContext db, IModerationService moderation) = Seed(
            Result.Failure<List<UnbanRequestDto>>("Missing scope.", "TWITCH_SCOPE_MISSING")
        );

        Result<List<ActionRequiredItemDto>> items = await new UnbanRequestSource(
            db,
            moderation
        ).GetItemsAsync(Channel, new HashSet<string>());

        items.IsFailure.Should().BeTrue();
        items.ErrorCode.Should().Be("TWITCH_SCOPE_MISSING");
    }

    [Fact]
    public async Task The_inbox_reports_a_failed_unban_read_as_one_source_unavailable_item()
    {
        (ActionRequiredInboxServiceTestDbContext db, IModerationService moderation) = Seed(
            Result.Failure<List<UnbanRequestDto>>("Helix down.", "TWITCH_ERROR")
        );
        ActionRequiredInboxService inbox = new(
            [new UnbanRequestSource(db, moderation)],
            db,
            TimeProvider.System,
            new RecordingChangeNotifier(),
            NullLogger<ActionRequiredInboxService>.Instance
        );

        Result<List<ActionRequiredItemDto>> items = await inbox.GetItemsAsync(Channel);

        items.IsSuccess.Should().BeTrue();
        ActionRequiredItemDto item = items.Value.Should().ContainSingle().Subject;
        item.Id.Should().Be("source-unavailable:unban_requests");
        item.Kind.Should().Be("source_unavailable");
        item.Parameters.Should()
            .Contain("source", "unban_requests")
            .And.Contain("reason", "Helix down.");
        item.DeepLinkRoute.Should().Be("integrations");
    }

    [Fact]
    public async Task A_channel_that_does_not_exist_is_a_failure()
    {
        (ActionRequiredInboxServiceTestDbContext db, IModerationService moderation) = Seed(
            Result.Success<List<UnbanRequestDto>>([])
        );

        Result<List<ActionRequiredItemDto>> items = await new UnbanRequestSource(
            db,
            moderation
        ).GetItemsAsync(Guid.NewGuid(), new HashSet<string>());

        items.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Dismissing_an_appeal_resolves_to_its_own_key()
    {
        (ActionRequiredInboxServiceTestDbContext db, IModerationService moderation) = Seed(
            Result.Success<List<UnbanRequestDto>>([])
        );

        Result<List<string>> keys = await new UnbanRequestSource(
            db,
            moderation
        ).ResolveDismissalKeysAsync(Channel, "unban:req-1");

        keys.Value.Should().Equal("unban:req-1");
    }

    [Fact]
    public void The_source_owns_the_unban_prefix_and_refreshes_on_both_unban_events()
    {
        (ActionRequiredInboxServiceTestDbContext db, IModerationService moderation) = Seed(
            Result.Success<List<UnbanRequestDto>>([])
        );

        IActionRequiredSource source = new UnbanRequestSource(db, moderation);

        source.KeyPrefixes.Should().Equal("unban:");
        source
            .InvalidatingEventTypes.Should()
            .BeEquivalentTo(nameof(UnbanRequestCreatedEvent), nameof(UnbanRequestResolvedEvent));
    }

    [Theory]
    [InlineData(nameof(UnbanRequestCreatedEvent))]
    [InlineData(nameof(UnbanRequestResolvedEvent))]
    public async Task A_journaled_unban_event_refreshes_the_channels_inbox_live(string eventType)
    {
        (ActionRequiredInboxServiceTestDbContext db, IModerationService moderation) = Seed(
            Result.Success<List<UnbanRequestDto>>([])
        );
        RecordingChangeNotifier notifier = new();
        ActionRequiredInvalidationHook hook = new(
            [new UnbanRequestSource(db, moderation)],
            notifier
        );

        await hook.OnCommittedAsync(
            new EventRecord(
                Id: 1,
                EventId: Guid.NewGuid(),
                BroadcasterId: Channel,
                StreamPosition: 1,
                EventType: eventType,
                EventVersion: 1,
                Source: "domain",
                PayloadJson: "{}",
                PayloadIsEncrypted: false,
                SubjectKeyId: null,
                CorrelationId: null,
                CausationId: null,
                ActorUserId: null,
                ActorExternalUserId: null,
                ActorProvider: null,
                MetadataJson: "{}",
                OccurredAt: DateTime.UtcNow,
                RecordedAt: DateTime.UtcNow
            )
        );

        notifier.Signalled.Should().Equal(Channel);
    }

    [Fact]
    public async Task A_new_appeal_publishes_a_unban_requests_config_push_for_its_channel()
    {
        CapturingEventBus bus = new();

        await new UnbanRequestCreatedConfigPushHandler(bus).HandleAsync(
            new UnbanRequestCreatedEvent
            {
                BroadcasterId = Channel,
                RequestId = "req-1",
                UserId = "7001",
                UserDisplayName = "BannedBob",
                UserLogin = "bannedbob",
                Text = "sorry",
            },
            CancellationToken.None
        );

        ChannelConfigChangedEvent push = bus.EventsOf<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        push.BroadcasterId.Should().Be(Channel);
        push.Domain.Should().Be("unban-requests");
        push.EntityId.Should().Be("req-1");
        push.Action.Should().Be("created");
    }

    [Fact]
    public async Task A_resolved_appeal_publishes_a_unban_requests_config_push_for_its_channel()
    {
        CapturingEventBus bus = new();

        await new UnbanRequestResolvedConfigPushHandler(bus).HandleAsync(
            new UnbanRequestResolvedEvent
            {
                BroadcasterId = Channel,
                RequestId = "req-1",
                UserId = "7001",
                UserDisplayName = "BannedBob",
                ModeratorId = "8001",
                ModeratorDisplayName = "ModMaya",
                Status = "approved",
                ResolutionText = "",
            },
            CancellationToken.None
        );

        ChannelConfigChangedEvent push = bus.EventsOf<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        push.BroadcasterId.Should().Be(Channel);
        push.Domain.Should().Be("unban-requests");
        push.EntityId.Should().Be("req-1");
        push.Action.Should().Be("updated");
    }
}
