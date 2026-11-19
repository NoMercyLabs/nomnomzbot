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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The moderator's own "remove spam in my name" switch (Stoney, 2026-11-19: "Your own yes is enough"): off
/// until they turn it on, stored on their own membership of that one channel, and refused for anyone who holds
/// no role there or for a channel whose owner's access already acts.
/// </summary>
public sealed class OwnNameSpamRemovalServiceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000f2"
    );
    private static readonly Guid ModeratorId = Guid.Parse("0192b000-0000-7000-8000-0000000000f3");
    private static readonly Guid StrangerId = Guid.Parse("0192b000-0000-7000-8000-0000000000f4");

    private static async Task<PipelineOptionsTestDbContext> SeedAsync(
        bool onboarded = false,
        DateTime? membershipDeletedAt = null
    )
    {
        PipelineOptionsTestDbContext db = PipelineOptionsTestDbContext.New();
        db.Channels.AddRange(
            new()
            {
                Id = ChannelId,
                Name = "Resident_Meeper",
                NameNormalized = "resident_meeper",
                TwitchChannelId = "1403633423",
                IsOnboarded = onboarded,
            },
            new()
            {
                Id = OtherChannelId,
                Name = "OtherStreamer",
                NameNormalized = "otherstreamer",
                TwitchChannelId = "200",
                IsOnboarded = false,
            }
        );
        db.ChannelMemberships.AddRange(
            Membership(ChannelId, membershipDeletedAt),
            Membership(OtherChannelId, null)
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static ChannelMembership Membership(Guid channelId, DateTime? deletedAt) =>
        new()
        {
            BroadcasterId = channelId,
            UserId = ModeratorId,
            ManagementRole = ManagementRole.Moderator,
            GrantedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            DeletedAt = deletedAt,
        };

    private static Task<bool> StoredAsync(PipelineOptionsTestDbContext db, Guid channelId) =>
        db
            .ChannelMemberships.IgnoreQueryFilters()
            .Where(m => m.BroadcasterId == channelId && m.UserId == ModeratorId)
            .Select(m => m.RemovesSpamInOwnName)
            .SingleAsync();

    [Fact]
    public async Task The_switch_is_off_until_the_moderator_turns_it_on()
    {
        await using PipelineOptionsTestDbContext db = await SeedAsync();

        OwnNameSpamRemovalDto state = await new OwnNameSpamRemovalService(db).GetAsync(
            ChannelId,
            ModeratorId
        );

        state.Should().Be(new OwnNameSpamRemovalDto(Enabled: false, Applies: true));
    }

    [Fact]
    public async Task Turning_it_on_stores_the_yes_on_that_channel_only_and_reads_back_on()
    {
        await using PipelineOptionsTestDbContext db = await SeedAsync();
        OwnNameSpamRemovalService service = new(db);

        Result<OwnNameSpamRemovalDto> result = await service.SetAsync(
            ChannelId,
            ModeratorId,
            enabled: true
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new OwnNameSpamRemovalDto(Enabled: true, Applies: true));
        (await StoredAsync(db, ChannelId)).Should().BeTrue();
        (await StoredAsync(db, OtherChannelId)).Should().BeFalse();
        (await service.GetAsync(ChannelId, ModeratorId)).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Turning_it_off_again_removes_the_yes()
    {
        await using PipelineOptionsTestDbContext db = await SeedAsync();
        OwnNameSpamRemovalService service = new(db);
        await service.SetAsync(ChannelId, ModeratorId, enabled: true);

        Result<OwnNameSpamRemovalDto> result = await service.SetAsync(
            ChannelId,
            ModeratorId,
            enabled: false
        );

        result.Value.Should().Be(new OwnNameSpamRemovalDto(Enabled: false, Applies: true));
        (await StoredAsync(db, ChannelId)).Should().BeFalse();
    }

    [Fact]
    public async Task Someone_with_no_role_on_the_channel_is_refused_and_nothing_is_stored()
    {
        await using PipelineOptionsTestDbContext db = await SeedAsync();
        OwnNameSpamRemovalService service = new(db);

        Result<OwnNameSpamRemovalDto> result = await service.SetAsync(
            ChannelId,
            StrangerId,
            enabled: true
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        (await StoredAsync(db, ChannelId)).Should().BeFalse();
        (await service.GetAsync(ChannelId, StrangerId))
            .Should()
            .Be(new OwnNameSpamRemovalDto(Enabled: false, Applies: false));
    }

    [Fact]
    public async Task A_moderator_whose_role_was_removed_is_refused()
    {
        await using PipelineOptionsTestDbContext db = await SeedAsync(
            membershipDeletedAt: new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc)
        );

        Result<OwnNameSpamRemovalDto> result = await new OwnNameSpamRemovalService(db).SetAsync(
            ChannelId,
            ModeratorId,
            enabled: true
        );

        result.ErrorCode.Should().Be("FORBIDDEN");
        (await StoredAsync(db, ChannelId)).Should().BeFalse();
    }

    [Fact]
    public async Task An_onboarded_channel_refuses_the_switch_because_its_owner_access_acts_there()
    {
        await using PipelineOptionsTestDbContext db = await SeedAsync(onboarded: true);
        OwnNameSpamRemovalService service = new(db);

        Result<OwnNameSpamRemovalDto> result = await service.SetAsync(
            ChannelId,
            ModeratorId,
            enabled: true
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await StoredAsync(db, ChannelId)).Should().BeFalse();
        (await service.GetAsync(ChannelId, ModeratorId)).Applies.Should().BeFalse();
    }
}
