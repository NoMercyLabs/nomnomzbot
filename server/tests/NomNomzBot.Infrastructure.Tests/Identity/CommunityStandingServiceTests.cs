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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves Plane-A standing writes (roles-permissions §3.5): an upsert recomputes the ladder level; the change
/// event fires only when the standing actually moves (quiet hot path for repeat viewers); and a viewer with no
/// recorded standing reads as <see cref="CommunityStanding.Everyone"/>.
/// </summary>
public sealed class CommunityStandingServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f0");
    private static readonly Guid User = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (CommunityStandingService Sut, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        CommunityStandingService sut = new(db, bus, new FakeTimeProvider(Now));
        return (sut, bus);
    }

    [Fact]
    public async Task Upsert_records_standing_recomputes_level_and_emits_on_first_observation()
    {
        (CommunityStandingService sut, RecordingEventBus bus) = Build();

        await sut.UpsertStandingAsync(
            Channel,
            User,
            CommunityStanding.Vip,
            StandingSource.ChatTags,
            subTier: null
        );

        (await sut.GetStandingAsync(Channel, User)).Value.Should().Be(CommunityStanding.Vip);
        CommunityStandingChangedEvent evt = bus
            .Published.OfType<CommunityStandingChangedEvent>()
            .Single();
        evt.OldStanding.Should().Be(CommunityStanding.Everyone);
        evt.NewStanding.Should().Be(CommunityStanding.Vip);
    }

    [Fact]
    public async Task Upsert_with_unchanged_standing_emits_no_event()
    {
        (CommunityStandingService sut, RecordingEventBus bus) = Build();
        await sut.UpsertStandingAsync(
            Channel,
            User,
            CommunityStanding.Subscriber,
            StandingSource.ChatTags,
            "1000"
        );
        bus.Published.Clear();

        await sut.UpsertStandingAsync(
            Channel,
            User,
            CommunityStanding.Subscriber,
            StandingSource.EventSubBadge,
            "1000"
        );

        bus.Published.OfType<CommunityStandingChangedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_emits_on_a_standing_change()
    {
        (CommunityStandingService sut, RecordingEventBus bus) = Build();
        await sut.UpsertStandingAsync(
            Channel,
            User,
            CommunityStanding.Subscriber,
            StandingSource.ChatTags,
            "1000"
        );
        bus.Published.Clear();

        await sut.UpsertStandingAsync(
            Channel,
            User,
            CommunityStanding.Vip,
            StandingSource.ChatTags,
            subTier: null
        );

        CommunityStandingChangedEvent evt = bus
            .Published.OfType<CommunityStandingChangedEvent>()
            .Single();
        evt.OldStanding.Should().Be(CommunityStanding.Subscriber);
        evt.NewStanding.Should().Be(CommunityStanding.Vip);
    }

    [Fact]
    public async Task GetStanding_is_Everyone_when_none_recorded()
    {
        (CommunityStandingService sut, _) = Build();

        (await sut.GetStandingAsync(Channel, User)).Value.Should().Be(CommunityStanding.Everyone);
    }
}
