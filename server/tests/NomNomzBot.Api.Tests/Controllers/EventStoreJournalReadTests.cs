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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The journal read: it asks for the resolved channel's own stream only, passes the filters and the page through,
/// and answers rows without payloads plus the channel's head position.
/// </summary>
public sealed class EventStoreJournalReadTests
{
    private static readonly Guid Channel = Guid.Parse("019f2d00-1111-7000-8000-000000000001");

    private static EventRecord Row(long position) =>
        new(
            Id: position,
            EventId: Guid.NewGuid(),
            BroadcasterId: Channel,
            StreamPosition: position,
            EventType: "channel.follow",
            EventVersion: 1,
            Source: "eventsub",
            PayloadJson: "{\"secret\":\"sealed\"}",
            PayloadIsEncrypted: true,
            SubjectKeyId: Guid.NewGuid(),
            CorrelationId: null,
            CausationId: null,
            ActorUserId: null,
            ActorExternalUserId: "123",
            ActorProvider: "twitch",
            MetadataJson: "{}",
            OccurredAt: new(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc),
            RecordedAt: new(2026, 6, 20, 11, 0, 1, DateTimeKind.Utc)
        );

    private static (EventStoreController, IEventJournal) Build(Guid? tenantId = null)
    {
        IEventJournal journal = Substitute.For<IEventJournal>();
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(tenantId ?? Channel);
        journal
            .QueryAsync(Arg.Any<EventJournalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PagedList<EventRecord>([Row(7), Row(6)], 2, 2, 5)));
        journal
            .GetHeadPositionAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(7L));
        EventStoreController controller = new(
            journal,
            Substitute.For<IEventJournalPortabilityService>(),
            Substitute.For<ILegacyChannelImportService>(),
            Substitute.For<IProjectionRunner>(),
            tenant,
            TimeProvider.System
        );
        return (controller, journal);
    }

    [Fact]
    public async Task The_query_is_scoped_to_the_resolved_channel_and_carries_every_filter()
    {
        (EventStoreController controller, IEventJournal journal) = Build();
        DateTime from = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime to = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        await controller.ReadJournal(
            Channel.ToString(),
            "channel.follow",
            "eventsub",
            from,
            to,
            new PageRequestDto { Page = 2, Take = 2 },
            CancellationToken.None
        );

        await journal
            .Received(1)
            .QueryAsync(
                Arg.Is<EventJournalQuery>(q =>
                    q.BroadcasterId == Channel
                    && q.EventType == "channel.follow"
                    && q.Source == "eventsub"
                    && q.FromUtc == from
                    && q.ToUtc == to
                    && q.Page == 2
                    && q.PageSize == 2
                ),
                Arg.Any<CancellationToken>()
            );
        await journal.Received(1).GetHeadPositionAsync(Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_page_lists_rows_without_payloads_and_reports_total_head_and_more()
    {
        (EventStoreController controller, _) = Build();

        IActionResult result = await controller.ReadJournal(
            Channel.ToString(),
            null,
            null,
            null,
            null,
            new PageRequestDto { Page = 2, Take = 2 },
            CancellationToken.None
        );

        EventJournalPageDto page = result
            .Should()
            .BeOfType<OkObjectResult>()
            .Which.Value.Should()
            .BeOfType<StatusResponseDto<EventJournalPageDto>>()
            .Which.Data!;
        page.Items.Select(i => i.StreamPosition).Should().Equal(7, 6);
        page.Items[0]
            .Should()
            .Match<EventJournalEntryDto>(i =>
                i.EventType == "channel.follow"
                && i.Source == "eventsub"
                && i.ActorExternalUserId == "123"
                && i.ActorProvider == "twitch"
            );
        typeof(EventJournalEntryDto)
            .GetProperties()
            .Select(p => p.Name)
            .Should()
            .NotContain(["PayloadJson", "Payload", "MetadataJson"]);
        page.TotalCount.Should().Be(5);
        page.HeadPosition.Should().Be(7);
        page.HasMore.Should().BeTrue("page 2 of size 2 over 5 rows has a third page");
    }

    [Fact]
    public async Task With_no_resolved_channel_nothing_is_read()
    {
        IEventJournal journal = Substitute.For<IEventJournal>();
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns((Guid?)null);
        EventStoreController controller = new(
            journal,
            Substitute.For<IEventJournalPortabilityService>(),
            Substitute.For<ILegacyChannelImportService>(),
            Substitute.For<IProjectionRunner>(),
            tenant,
            TimeProvider.System
        );

        IActionResult result = await controller.ReadJournal(
            "x",
            null,
            null,
            null,
            null,
            new PageRequestDto(),
            CancellationToken.None
        );

        result.Should().NotBeOfType<OkObjectResult>();
        journal.ReceivedCalls().Should().BeEmpty();
    }
}
