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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Seeding;

/// <summary>
/// Proves the startup backfill re-fires the onboarding seed pipeline for already-onboarded channels: it
/// publishes exactly one <c>ChannelOnboardedEvent</c> per onboarded channel — carrying that channel's real
/// identity fields — and skips channels that never onboarded, so the seed handlers run for existing installs
/// (e.g. stoney, who onboarded before the pipeline existed) without re-firing for un-onboarded rows.
/// </summary>
public sealed class OnboardedChannelSeedBackfillServiceTests
{
    [Fact]
    public async Task Backfill_publishes_one_event_per_onboarded_channel_with_its_identity()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid onboardedA = Guid.Parse("0192a000-0000-7000-8000-00000000d001");
        Guid onboardedB = Guid.Parse("0192a000-0000-7000-8000-00000000d002");
        Guid notOnboarded = Guid.Parse("0192a000-0000-7000-8000-00000000d003");

        db.Channels.Add(Channel(onboardedA, "tw-a", "stoney", isOnboarded: true));
        db.Channels.Add(Channel(onboardedB, "tw-b", "second", isOnboarded: true));
        db.Channels.Add(Channel(notOnboarded, "tw-c", "draft", isOnboarded: false));
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        OnboardedChannelSeedBackfillService sut = new(
            new SingleContextScopeFactory(db),
            bus,
            NullLogger<OnboardedChannelSeedBackfillService>.Instance
        );

        await sut.StartAsync(CancellationToken.None);
        if (sut.ExecuteTask is not null)
            await sut.ExecuteTask;
        await sut.StopAsync(CancellationToken.None);

        List<ChannelOnboardedEvent> published = bus
            .Published.OfType<ChannelOnboardedEvent>()
            .ToList();

        published.Should().HaveCount(2);
        published.Select(e => e.BroadcasterId).Should().BeEquivalentTo([onboardedA, onboardedB]);
        published.Should().NotContain(e => e.BroadcasterId == notOnboarded);

        ChannelOnboardedEvent a = published.Single(e => e.BroadcasterId == onboardedA);
        a.TwitchChannelId.Should().Be("tw-a");
        a.Name.Should().Be("stoney");
    }

    [Fact]
    public async Task Backfill_publishes_nothing_when_no_channel_has_onboarded()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            Channel(
                Guid.Parse("0192a000-0000-7000-8000-00000000d010"),
                "tw-x",
                "draft",
                isOnboarded: false
            )
        );
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        OnboardedChannelSeedBackfillService sut = new(
            new SingleContextScopeFactory(db),
            bus,
            NullLogger<OnboardedChannelSeedBackfillService>.Instance
        );

        await sut.StartAsync(CancellationToken.None);
        if (sut.ExecuteTask is not null)
            await sut.ExecuteTask;
        await sut.StopAsync(CancellationToken.None);

        bus.Published.OfType<ChannelOnboardedEvent>().Should().BeEmpty();
    }

    private static Channel Channel(Guid id, string twitchId, string name, bool isOnboarded) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            TwitchChannelId = twitchId,
            Name = name,
            NameNormalized = name,
            IsOnboarded = isOnboarded,
        };

    /// <summary>A scope factory whose every scope resolves the one shared test <see cref="AuthDbContext"/>.</summary>
    private sealed class SingleContextScopeFactory(IApplicationDbContext db) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(db);

        private sealed class Scope(IApplicationDbContext db) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(IApplicationDbContext) ? db : null;

            public void Dispose() { }
        }
    }
}
