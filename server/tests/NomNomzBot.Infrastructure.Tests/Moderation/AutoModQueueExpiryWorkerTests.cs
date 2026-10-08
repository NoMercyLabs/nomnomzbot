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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the AutoMod queue backstop: <see cref="AutoModQueueExpiryWorker"/>'s single sweep closes a pending
/// AutoMod row whose hold is older than the backstop (and tells the dashboard), leaves every other row alone,
/// and is a clean no-op when another instance holds the sweep lease.
/// </summary>
public sealed class AutoModQueueExpiryWorkerTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8a");

    private static (
        AutoModQueueExpiryWorker Worker,
        ModerationServiceTestDbContext Db,
        IEventBus Events,
        FakeTimeProvider Clock
    ) Build(bool leaseHeldElsewhere = false)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        IEventBus events = Substitute.For<IEventBus>();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        ModerationQueueService queue = new(
            db,
            Substitute.For<IUserService>(),
            Substitute.For<ITwitchModerationApi>(),
            Substitute.For<IModerationService>(),
            events,
            clock,
            NullLogger<ModerationQueueService>.Instance
        );
        IRunOnceGuard guard = Substitute.For<IRunOnceGuard>();
        guard
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(leaseHeldElsewhere ? null : Substitute.For<IAsyncDisposable>());
        ServiceProvider provider = new ServiceCollection()
            .AddScoped<IRunOnceGuard>(_ => guard)
            .AddScoped<IModerationQueueService>(_ => queue)
            .BuildServiceProvider();
        AutoModQueueExpiryWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<AutoModQueueExpiryWorker>.Instance
        );
        return (worker, db, events, clock);
    }

    private static ModerationQueueItem Row(
        string messageId,
        ModerationQueueSource source,
        DateTime createdAt,
        ModerationQueueStatus status = ModerationQueueStatus.Pending
    ) =>
        new()
        {
            BroadcasterId = Tenant,
            Source = source,
            Status = status,
            AutoModMessageId = messageId,
            TargetTwitchUserId = "9001",
            TargetUsernameSnapshot = "chatter",
            CreatedAt = createdAt,
        };

    [Fact]
    public async Task The_sweep_expires_only_stale_pending_AutoMod_rows_and_tells_the_dashboard()
    {
        (
            AutoModQueueExpiryWorker worker,
            ModerationServiceTestDbContext db,
            IEventBus events,
            FakeTimeProvider clock
        ) = Build();
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime stale = now - AutoModQueueExpiryWorker.StaleHoldBackstop - TimeSpan.FromMinutes(1);
        db.ModerationQueueItems.AddRange(
            Row("stale", ModerationQueueSource.AutoMod, stale),
            Row("fresh", ModerationQueueSource.AutoMod, now - TimeSpan.FromMinutes(5)),
            Row("stale-filter", ModerationQueueSource.ChatFilter, stale),
            Row("stale-heat", ModerationQueueSource.HeatThreshold, stale),
            Row(
                "stale-approved",
                ModerationQueueSource.AutoMod,
                stale,
                ModerationQueueStatus.Approved
            )
        );
        await db.SaveChangesAsync();

        await worker.SweepAsync(CancellationToken.None);

        Dictionary<string, ModerationQueueItem> byId =
            await db.ModerationQueueItems.ToDictionaryAsync(i => i.AutoModMessageId!);
        byId["stale"].Status.Should().Be(ModerationQueueStatus.Expired);
        byId["stale"].ResolutionAction.Should().Be("expired");
        byId["stale"].ResolvedAt.Should().Be(now);
        byId["stale"].ResolvedByUserId.Should().BeNull();
        byId["fresh"].Status.Should().Be(ModerationQueueStatus.Pending);
        byId["stale-filter"].Status.Should().Be(ModerationQueueStatus.Pending);
        byId["stale-heat"].Status.Should().Be(ModerationQueueStatus.Pending);
        byId["stale-approved"].Status.Should().Be(ModerationQueueStatus.Approved);
        await events
            .Received(1)
            .PublishAsync(
                Arg.Is<AutoModMessageUpdatedEvent>(e =>
                    e.BroadcasterId == Tenant
                    && e.MessageId == "stale"
                    && e.UserId == "9001"
                    && e.Status == "expired"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task The_sweep_is_idempotent_and_does_nothing_when_another_instance_holds_the_lease()
    {
        (
            AutoModQueueExpiryWorker worker,
            ModerationServiceTestDbContext db,
            IEventBus events,
            FakeTimeProvider clock
        ) = Build();
        db.ModerationQueueItems.Add(
            Row(
                "stale",
                ModerationQueueSource.AutoMod,
                clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(5)
            )
        );
        await db.SaveChangesAsync();

        await worker.SweepAsync(CancellationToken.None);
        await worker.SweepAsync(CancellationToken.None);

        await events
            .Received(1)
            .PublishAsync(Arg.Any<AutoModMessageUpdatedEvent>(), Arg.Any<CancellationToken>());

        (
            AutoModQueueExpiryWorker blocked,
            ModerationServiceTestDbContext db2,
            IEventBus events2,
            FakeTimeProvider clock2
        ) = Build(leaseHeldElsewhere: true);
        db2.ModerationQueueItems.Add(
            Row(
                "stale",
                ModerationQueueSource.AutoMod,
                clock2.GetUtcNow().UtcDateTime - TimeSpan.FromHours(5)
            )
        );
        await db2.SaveChangesAsync();

        await blocked.SweepAsync(CancellationToken.None);

        (await db2.ModerationQueueItems.SingleAsync())
            .Status.Should()
            .Be(ModerationQueueStatus.Pending);
        await events2
            .DidNotReceive()
            .PublishAsync(Arg.Any<AutoModMessageUpdatedEvent>(), Arg.Any<CancellationToken>());
    }
}
