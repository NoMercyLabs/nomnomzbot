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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Giveaways;

/// <summary>
/// Proves the `ClosesAt`-schedule enforcement (giveaways.md): an open giveaway whose
/// <c>ScheduledCloseAt</c> has passed closes automatically, exactly as a manual
/// <c>GiveawayService.CloseAsync</c> would — status flips to <c>closed</c> and <c>ClosesAt</c> is
/// stamped to the real close moment, never left at the scheduled target.
/// </summary>
public sealed class GiveawayAutoCloseWorkerTests
{
    private static readonly Guid Tenant = Guid.Parse("0193b000-0000-7000-8000-0000000000b1");
    private static readonly Guid Owner = Guid.Parse("0193b000-0000-7000-8000-0000000000b9");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (GiveawayAutoCloseWorker Worker, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Tenant,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-1",
                TwitchChannelId = "tw-1",
                Name = "streamer",
                NameNormalized = "streamer",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();

        FakeTimeProvider clock = new(Now);
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton<IRunOnceGuard>(new SharedFakeRunOnceGuard())
            .BuildServiceProvider();

        GiveawayAutoCloseWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<GiveawayAutoCloseWorker>.Instance
        );
        return (worker, db);
    }

    private static Giveaway SeedOpenGiveaway(AuthDbContext db, DateTime? scheduledCloseAt)
    {
        Giveaway giveaway = new()
        {
            BroadcasterId = Tenant,
            Title = "Auto-close me",
            EntryMode = GiveawayEntryMode.Keyword,
            Keyword = "!win",
            PrizeMode = GiveawayPrizeMode.Announce,
            Status = GiveawayStatus.Open,
            OpenedAt = Now.UtcDateTime.AddHours(-1),
            ScheduledCloseAt = scheduledCloseAt,
        };
        db.Giveaways.Add(giveaway);
        db.SaveChanges();
        return giveaway;
    }

    [Fact]
    public async Task An_open_giveaway_past_its_scheduled_close_time_closes_like_a_manual_close()
    {
        (GiveawayAutoCloseWorker worker, AuthDbContext db) = Build();
        Giveaway giveaway = SeedOpenGiveaway(db, Now.UtcDateTime.AddMinutes(-1));

        await worker.SweepAsync(CancellationToken.None);

        Giveaway reloaded = db.Giveaways.Single(g => g.Id == giveaway.Id);
        reloaded.Status.Should().Be(GiveawayStatus.Closed);
        reloaded
            .ClosesAt.Should()
            .Be(Now.UtcDateTime, "ClosesAt records the REAL close moment, not the target");
    }

    [Fact]
    public async Task A_giveaway_not_yet_at_its_scheduled_close_time_stays_open()
    {
        (GiveawayAutoCloseWorker worker, AuthDbContext db) = Build();
        Giveaway giveaway = SeedOpenGiveaway(db, Now.UtcDateTime.AddMinutes(1));

        await worker.SweepAsync(CancellationToken.None);

        db.Giveaways.Single(g => g.Id == giveaway.Id).Status.Should().Be(GiveawayStatus.Open);
    }

    [Fact]
    public async Task A_giveaway_with_no_schedule_is_never_touched()
    {
        (GiveawayAutoCloseWorker worker, AuthDbContext db) = Build();
        Giveaway giveaway = SeedOpenGiveaway(db, scheduledCloseAt: null);

        await worker.SweepAsync(CancellationToken.None);

        db.Giveaways.Single(g => g.Id == giveaway.Id).Status.Should().Be(GiveawayStatus.Open);
    }
}
