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
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Notifications;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Proves the live-push half of the inbox (plan item A0 slice 3): a journaled event one of the sources declares
/// signals its channel, an unrelated or platform-wide event signals nothing, and a dismissal that wrote rows
/// signals the channel while a no-op dismissal does not.
/// </summary>
public sealed class ActionRequiredInvalidationTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000c1");

    [Theory]
    [InlineData("OutboundWebhookAutoDisabledEvent")]
    [InlineData("WidgetBuildFailedEvent")]
    [InlineData("EventSubSubscriptionStatusChangedEvent")]
    [InlineData("SongRequestLostAtProviderEvent")]
    [InlineData("IntegrationNeedsReauthEvent")]
    public async Task AnEventASourceDeclares_SignalsItsChannel(string eventType)
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        RecordingChangeNotifier notifier = new();
        ActionRequiredInvalidationHook hook = new(
            ActionRequiredInboxHarness.Sources(db, TimeProvider.System),
            notifier
        );

        await hook.OnCommittedAsync(Record(eventType, ChannelId));

        notifier.Signalled.Should().Equal(ChannelId);
    }

    [Fact]
    public async Task AnUnrelatedOrPlatformWideEvent_SignalsNothing()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        RecordingChangeNotifier notifier = new();
        ActionRequiredInvalidationHook hook = new(
            ActionRequiredInboxHarness.Sources(db, TimeProvider.System),
            notifier
        );

        await hook.OnCommittedAsync(Record("ChatMessageReceivedEvent", ChannelId));
        await hook.OnCommittedAsync(Record("WidgetBuildFailedEvent", null));

        notifier.Signalled.Should().BeEmpty();
    }

    [Fact]
    public async Task ADismissalThatWritesRows_SignalsTheChannel_AndANoOpDismissalDoesNot()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        ModerationQueueItem hold = new()
        {
            BroadcasterId = ChannelId,
            Source = ModerationQueueSource.AutoMod,
            Status = ModerationQueueStatus.Pending,
        };
        db.ModerationQueueItems.Add(hold);
        await db.SaveChangesAsync();
        RecordingChangeNotifier notifier = new();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db, notifier: notifier);

        await sut.DismissAsync(ChannelId, Guid.NewGuid(), [$"held:{hold.Id}"]);
        await sut.DismissAsync(ChannelId, Guid.NewGuid(), [$"held:{hold.Id}"]);

        notifier.Signalled.Should().Equal(ChannelId);
    }

    private static EventRecord Record(string eventType, Guid? channelId) =>
        new(
            Id: 1,
            EventId: Guid.NewGuid(),
            BroadcasterId: channelId,
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
        );
}
