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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the audited single-onboarding-path fix: <see cref="ChannelService.OnboardAsync"/> — the manual
/// <c>POST /channels</c> path — now publishes <see cref="ChannelOnboardedEvent"/> itself, exactly like the
/// Twitch-OAuth login path (<c>AuthService</c>), so the same 12 auto-discovered onboarding seed handlers run
/// regardless of which path onboarded the channel. Covers both branches: a brand-new channel AND the
/// idempotent "repair" branch for a channel that already exists (e.g. re-onboarding after
/// <c>IsOnboarded</c> was reset) — both must publish, and neither must ever create a second <c>Channel</c> row
/// for the same owner.
/// </summary>
public sealed class ChannelServiceOnboardingEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OnboardAsync_publishes_ChannelOnboardedEvent_for_a_brand_new_channel()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = Guid.Parse("0192a000-0000-7000-8000-00000000c101");
        db.Users.Add(
            new User
            {
                Id = ownerId,
                TwitchUserId = "tw-owner-1",
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
            }
        );
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        ChannelService sut = new(db, new FakeTimeProvider(Now), bus);

        Result<ChannelDto> result = await sut.OnboardAsync(
            ownerId.ToString(),
            new CreateChannelRequest { BroadcasterId = ownerId.ToString() }
        );

        result.IsSuccess.Should().BeTrue();

        List<ChannelOnboardedEvent> published = bus
            .Published.OfType<ChannelOnboardedEvent>()
            .ToList();
        published.Should().HaveCount(1);
        published[0].BroadcasterId.Should().Be(Guid.Parse(result.Value.Id));
        published[0].OwnerUserId.Should().Be(ownerId);
        published[0].TwitchChannelId.Should().Be("tw-owner-1");
        published[0].Name.Should().Be("stoney");

        (await db.Channels.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OnboardAsync_republishes_for_the_idempotent_repair_of_an_already_onboarded_channel()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = Guid.Parse("0192a000-0000-7000-8000-00000000c102");
        Guid channelId = Guid.Parse("0192a000-0000-7000-8000-00000000c103");
        db.Users.Add(
            new User
            {
                Id = ownerId,
                TwitchUserId = "tw-owner-2",
                Username = "existing",
                UsernameNormalized = "existing",
                DisplayName = "Existing",
            }
        );
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = ownerId,
                TwitchChannelId = "tw-owner-2",
                Name = "existing",
                NameNormalized = "existing",
                IsOnboarded = true,
            }
        );
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        ChannelService sut = new(db, new FakeTimeProvider(Now), bus);

        Result<ChannelDto> result = await sut.OnboardAsync(
            ownerId.ToString(),
            new CreateChannelRequest { BroadcasterId = ownerId.ToString() }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(channelId.ToString());

        List<ChannelOnboardedEvent> published = bus
            .Published.OfType<ChannelOnboardedEvent>()
            .ToList();
        published.Should().HaveCount(1);
        published[0].BroadcasterId.Should().Be(channelId);

        // The repair path re-onboards the SAME channel — it must never mint a second row for the owner.
        (await db.Channels.CountAsync())
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task OnboardAsync_called_twice_publishes_twice_and_still_never_duplicates_the_channel()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid ownerId = Guid.Parse("0192a000-0000-7000-8000-00000000c104");
        db.Users.Add(
            new User
            {
                Id = ownerId,
                TwitchUserId = "tw-owner-3",
                Username = "repeatonboard",
                UsernameNormalized = "repeatonboard",
                DisplayName = "RepeatOnboard",
            }
        );
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        ChannelService sut = new(db, new FakeTimeProvider(Now), bus);
        CreateChannelRequest request = new() { BroadcasterId = ownerId.ToString() };

        Result<ChannelDto> first = await sut.OnboardAsync(ownerId.ToString(), request);
        Result<ChannelDto> second = await sut.OnboardAsync(ownerId.ToString(), request);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.Id.Should().Be(first.Value.Id);

        bus.Published.OfType<ChannelOnboardedEvent>().Should().HaveCount(2);
        (await db.Channels.CountAsync()).Should().Be(1);
    }
}
