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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Proves what the inbox itself owns on top of its sources: one failing source never blanks the others, a
/// dismiss is routed to the source that minted the id, and an id no source owns is refused.
/// </summary>
public sealed class ActionRequiredInboxAggregationTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");

    [Fact]
    public async Task GetItemsAsync_KeepsTheOtherSourcesItems_WhenOneSourceFails()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = ChannelId,
                Provider = AuthEnums.IntegrationProvider.Spotify,
                Status = AuthEnums.IntegrationStatus.Expired,
                LastErrorAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = new(
            [new FailingSource(), new DeadIntegrationTokenSource(db)],
            db,
            TimeProvider.System,
            new RecordingChangeNotifier(),
            NullLogger<ActionRequiredInboxService>.Instance
        );

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        ActionRequiredItemDto item = result.Value.Single(i => i.Kind == "integration_token_dead");
        item.MessageKey.Should().Be("attention_integration_expired_message");
    }

    [Fact]
    public async Task GetItemsAsync_ReportsAFailedSourceAsOneItemNamingWhatCouldNotBeChecked()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        DateTime now = new(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc);
        ActionRequiredInboxService sut = new(
            [new FailingSource()],
            db,
            new FixedClock(now),
            new RecordingChangeNotifier(),
            NullLogger<ActionRequiredInboxService>.Instance
        );

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        ActionRequiredItemDto item = result.Value.Should().ContainSingle().Subject;
        item.Id.Should().Be("source-unavailable:failing_checks:2026-10-08");
        item.Kind.Should().Be("source_unavailable");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_source_unavailable_title");
        item.MessageKey.Should().Be("attention_source_unavailable_message");
        item.Parameters.Should()
            .HaveCount(2)
            .And.Contain("source", "failing_checks")
            .And.Contain("reason", "source is down");
        item.DetectedAt.Should().Be(now);
        item.DeepLinkRoute.Should().Be("integrations");
        item.Count.Should().Be(1);
        item.QueueItemIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetItemsAsync_HidesAFailedSourceItem_OnceTheStreamerDismissedIt()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        ActionRequiredInboxService sut = new(
            [new FailingSource()],
            db,
            new FixedClock(Today),
            new RecordingChangeNotifier(),
            NullLogger<ActionRequiredInboxService>.Instance
        );

        Result<int> dismissed = await sut.DismissAsync(
            ChannelId,
            Guid.NewGuid(),
            ["source-unavailable:failing_checks:2026-10-08"]
        );
        Result<List<ActionRequiredItemDto>> after = await sut.GetItemsAsync(ChannelId);

        dismissed.IsSuccess.Should().BeTrue();
        dismissed.Value.Should().Be(1);
        db.ActionRequiredDismissals.Single()
            .ItemKey.Should()
            .Be("source-unavailable:failing_checks:2026-10-08");
        after.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetItemsAsync_ShowsAFailedSourceAgainTheNextDay_WhenItIsStillFailing()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        FixedClock clock = new(Today);
        ActionRequiredInboxService sut = new(
            [new FailingSource()],
            db,
            clock,
            new RecordingChangeNotifier(),
            NullLogger<ActionRequiredInboxService>.Instance
        );
        await sut.DismissAsync(
            ChannelId,
            Guid.NewGuid(),
            ["source-unavailable:failing_checks:2026-10-08"]
        );

        clock.UtcNow = Today.AddDays(1);
        Result<List<ActionRequiredItemDto>> nextDay = await sut.GetItemsAsync(ChannelId);

        nextDay
            .Value.Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be("source-unavailable:failing_checks:2026-10-09");
    }

    [Fact]
    public async Task GetItemsAsync_KeepsAFailedSourceItemOnOtherChannels_WhenOneChannelDismissedIt()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        Guid otherChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000f2");
        ActionRequiredInboxService sut = new(
            [new FailingSource()],
            db,
            new FixedClock(Today),
            new RecordingChangeNotifier(),
            NullLogger<ActionRequiredInboxService>.Instance
        );
        await sut.DismissAsync(
            ChannelId,
            Guid.NewGuid(),
            ["source-unavailable:failing_checks:2026-10-08"]
        );

        Result<List<ActionRequiredItemDto>> other = await sut.GetItemsAsync(otherChannelId);

        other.Value.Should().ContainSingle().Which.Kind.Should().Be("source_unavailable");
    }

    [Fact]
    public async Task DismissAsync_RefusesAnIdNoSourceOwns_AndWritesNothing()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db);

        Result<int> result = await sut.DismissAsync(ChannelId, Guid.NewGuid(), ["bogus:123"]);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.ActionRequiredDismissals.Should().BeEmpty();
    }

    private static readonly DateTime Today = new(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
    }

    private sealed class FailingSource : IActionRequiredSource
    {
        public string SourceKey => "failing_checks";

        public IReadOnlyCollection<string> KeyPrefixes { get; } = ["failing:"];

        public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

        public Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
            Guid channelId,
            IReadOnlySet<string> dismissedKeys,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                Result.Failure<List<ActionRequiredItemDto>>("source is down", "INTERNAL_ERROR")
            );

        public Task<Result<List<string>>> ResolveDismissalKeysAsync(
            Guid channelId,
            string itemId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success<List<string>>([itemId]));
    }
}
