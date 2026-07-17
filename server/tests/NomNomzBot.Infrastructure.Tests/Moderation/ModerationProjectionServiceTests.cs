// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using RecordEntity = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the J.4/J.5 projections (moderation.md §3.8): actions roll up into the per-viewer history; heat
/// accrues its §3.8 deltas with 24h-half-life decay and fires the threshold event ONLY on an upward
/// crossing; trust re-derives from the rollup through the SHARED calculator (a banned viewer scores lower
/// than a clean one); an unknown subject projects nothing; and a rebuild reproduces the rollup from the
/// recorded actions with heat reset.
/// </summary>
public sealed class ModerationProjectionServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ee01");
    private static readonly Guid Subject = Guid.Parse("0192a000-0000-7000-8000-00000000ee02");
    private const string SubjectTwitchId = "viewer-42";
    private static readonly DateTime T0 = new(2026, 7, 17, 6, 0, 0, DateTimeKind.Utc);

    private static (
        ModerationProjectionService Sut,
        ModerationServiceTestDbContext Db,
        RecordingEventBus Bus
    ) Build(int threshold = 80)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .GetAutomodConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new AutomodConfigDto(
                        new AutomodLinkFilterDto(false, []),
                        new AutomodCapsFilterDto(false, 0),
                        new AutomodBannedPhrasesDto(false, []),
                        new AutomodEmoteSpamDto(false, 0),
                        threshold
                    )
                )
            );
        RecordingEventBus bus = new();
        ModerationProjectionService sut = new(
            db,
            moderation,
            bus,
            new FakeTimeProvider(new DateTimeOffset(T0)),
            NullLogger<ModerationProjectionService>.Instance
        );
        return (sut, db, bus);
    }

    private static async Task SeedSubjectAsync(ModerationServiceTestDbContext db)
    {
        db.Users.Add(
            new User
            {
                Id = Subject,
                TwitchUserId = SubjectTwitchId,
                Username = "viewer42",
                UsernameNormalized = "viewer42",
                DisplayName = "Viewer42",
                CreatedAt = T0.AddYears(-2), // 24 months of tenure — the trust base signal
            }
        );
        await db.SaveChangesAsync();
    }

    /// <summary>The clean-slate score for the same tenure the seeded subject has.</summary>
    private static double CleanScoreAt(DateTime nowUtc) =>
        Infrastructure.Music.TrustScoreCalculator.Calculate(
            new Infrastructure.Music.TrustContext
            {
                AccountAgeMonths = (nowUtc - T0.AddYears(-2)).TotalDays / 30.44,
            }
        );

    [Fact]
    public async Task A_ban_rolls_up_heats_40_and_drops_the_trust_below_a_clean_slate()
    {
        (ModerationProjectionService sut, ModerationServiceTestDbContext db, _) = Build();
        await SeedSubjectAsync(db);

        Result result = await sut.ApplyActionAsync(Channel, SubjectTwitchId, "ban", T0);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        UserModerationHistory history = await db.UserModerationHistories.SingleAsync();
        history.BanCount.Should().Be(1);
        history.LastActionType.Should().Be("ban");
        history.FirstSeenAt.Should().Be(T0);
        history.SubjectUserId.Should().Be(Subject);

        UserTrustScore score = await db.UserTrustScores.SingleAsync();
        score.HeatScore.Should().Be(40m, "a ban accrues +40 heat");
        score.LastHeatEventAt.Should().Be(T0);

        // The SHARED calculator penalizes the ban: a clean viewer of EQUAL tenure scores strictly higher.
        ((double)score.TrustScore)
            .Should()
            .BeLessThan(CleanScoreAt(T0));
    }

    [Fact]
    public async Task Heat_decays_with_a_24h_half_life_between_events()
    {
        (ModerationProjectionService sut, ModerationServiceTestDbContext db, _) = Build();
        await SeedSubjectAsync(db);

        await sut.ApplyActionAsync(Channel, SubjectTwitchId, "ban", T0); // heat 40
        await sut.ApplyActionAsync(Channel, SubjectTwitchId, "timeout", T0.AddHours(24)); // 20 + 15

        UserTrustScore score = await db.UserTrustScores.SingleAsync();
        score.HeatScore.Should().BeApproximately(35m, 0.01m, "40 halves to 20 over 24h, then +15");
        UserModerationHistory history = await db.UserModerationHistories.SingleAsync();
        history.BanCount.Should().Be(1);
        history.TimeoutCount.Should().Be(1);
    }

    [Fact]
    public async Task The_threshold_event_fires_only_on_the_upward_crossing()
    {
        (
            ModerationProjectionService sut,
            ModerationServiceTestDbContext db,
            RecordingEventBus bus
        ) = Build(threshold: 50);
        await SeedSubjectAsync(db);

        // 40 < 50: no event yet.
        await sut.ApplyActionAsync(Channel, SubjectTwitchId, "ban", T0);
        bus.Published.OfType<UserHeatThresholdCrossedEvent>().Should().BeEmpty();

        // ~40 + 40 = ~80 ≥ 50: the crossing — exactly one event.
        await sut.ApplyActionAsync(Channel, SubjectTwitchId, "ban", T0.AddMinutes(5));
        UserHeatThresholdCrossedEvent crossed = bus
            .Published.OfType<UserHeatThresholdCrossedEvent>()
            .Single();
        crossed.Threshold.Should().Be(50);
        crossed.SubjectUserId.Should().Be(Subject);

        // Still above the threshold: NO second event while heat stays high.
        await sut.ApplyActionAsync(Channel, SubjectTwitchId, "timeout", T0.AddMinutes(10));
        bus.Published.OfType<UserHeatThresholdCrossedEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task An_unknown_subject_and_a_heatless_action_project_sanely()
    {
        (ModerationProjectionService sut, ModerationServiceTestDbContext db, _) = Build();
        await SeedSubjectAsync(db);

        // Unknown Twitch id: skipped outright.
        (await sut.ApplyActionAsync(Channel, "stranger-99", "ban", T0))
            .IsSuccess.Should()
            .BeTrue();
        (await db.UserModerationHistories.CountAsync()).Should().Be(0);

        // A warn counts in the rollup but accrues no heat.
        await sut.ApplyActionAsync(Channel, SubjectTwitchId, "warn", T0);
        (await db.UserModerationHistories.SingleAsync()).WarningCount.Should().Be(1);
        (await db.UserTrustScores.SingleAsync()).HeatScore.Should().Be(0m);
    }

    [Fact]
    public async Task Rebuild_reproduces_the_rollup_from_recorded_actions_and_resets_heat()
    {
        (ModerationProjectionService sut, ModerationServiceTestDbContext db, _) = Build();
        await SeedSubjectAsync(db);
        // Live state accrued heat...
        await sut.ApplyActionAsync(Channel, SubjectTwitchId, "ban", T0);
        // ...and the durable record rows exist (one ban + one timeout).
        foreach (string action in new[] { "ban", "timeout" })
            db.Records.Add(
                new RecordEntity
                {
                    BroadcasterId = Channel,
                    RecordType = "moderation_action",
                    Data = JsonSerializer.Serialize(
                        new { Action = action, TargetUserId = SubjectTwitchId }
                    ),
                    UserId = "mod-1",
                    CreatedAt = T0,
                }
            );
        await db.SaveChangesAsync();

        Result result = await sut.RebuildAsync(Channel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        UserModerationHistory history = await db.UserModerationHistories.SingleAsync();
        history
            .BanCount.Should()
            .Be(1, "the rebuild counts the RECORDED actions, not the live rollup");
        history.TimeoutCount.Should().Be(1);
        UserTrustScore score = await db.UserTrustScores.SingleAsync();
        score.HeatScore.Should().Be(0m, "heat is transient state and resets on rebuild");
        ((double)score.TrustScore)
            .Should()
            .BeLessThan(CleanScoreAt(T0), "the rebuilt ban+timeout penalties apply");
    }
}
