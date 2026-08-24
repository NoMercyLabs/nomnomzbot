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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;
using Record = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// S012: a moderation RULE (the pattern-matching auto-mod rules a broadcaster configures on the dashboard, not
/// the manual one-off timeout/ban) carries the exact same danger as the manual timeout box — an Action of
/// "timeout" with a missing/zero/negative <c>DurationSeconds</c>. <see cref="AutoModerationHandler"/>'s
/// enforcement switch only understands timeout/ban/delete, so an unrecognized Action would silently no-op
/// forever when the rule fires, and a non-positive duration would be forwarded to Twitch as-is. Both are
/// rejected here, at the point the rule is written, so a bad rule can never be saved in the first place —
/// proven on the PERSISTED record and the returned DTO, not merely on a boolean/exception.
/// </summary>
public sealed class ModerationServiceRuleValidationTests
{
    private const string RuleRecordType = "moderation_rule";
    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8c");
    private static string BroadcasterId => Tenant.ToString();

    private static ModerationService NewService(ModerationServiceTestDbContext db) =>
        new(
            db,
            Substitute.For<ITwitchModerationApi>(),
            Substitute.For<IChannelRegistry>(),
            TimeProvider.System,
            NullLogger<ModerationService>.Instance,
            Substitute.For<IEventBus>()
        );

    private static async Task SeedChannelAsync(ModerationServiceTestDbContext db)
    {
        db.Channels.Add(
            new()
            {
                Id = Tenant,
                TwitchChannelId = "1001",
                OwnerUserId = Guid.NewGuid(),
                Name = "c",
                NameNormalized = "c",
            }
        );
        await db.SaveChangesAsync();
    }

    // ─── CreateRuleAsync ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-30)]
    public async Task CreateRuleAsync_TimeoutActionWithoutAPositiveDuration_IsRejectedAndWritesNoRecord(
        int? durationSeconds
    )
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);

        Result<ModerationRuleDetail> result = await NewService(db)
            .CreateRuleAsync(
                BroadcasterId,
                new()
                {
                    Name = "spam-timeout",
                    Type = "banned_phrase",
                    Action = "timeout",
                    DurationSeconds = durationSeconds,
                }
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");

        // Pre-fix, this call persisted the rule as-is (no Action/DurationSeconds check existed at all) — an
        // armed "timeout" rule with no usable duration would later hit AutoModerationHandler's
        // `rule.DurationSeconds ?? 60` for the null case (safe) but forward 0/-30 straight to Twitch untouched
        // for the explicit-bad-value case. Prove nothing was ever written for any of the three inputs.
        (await db.Records.CountAsync(r => r.RecordType == RuleRecordType))
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task CreateRuleAsync_UnknownAction_IsRejectedAndWritesNoRecord()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);

        Result<ModerationRuleDetail> result = await NewService(db)
            .CreateRuleAsync(
                BroadcasterId,
                new()
                {
                    Name = "typo-rule",
                    Type = "banned_phrase",
                    Action = "bann", // typo — AutoModerationHandler's switch would silently no-op on this
                }
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Records.CountAsync(r => r.RecordType == RuleRecordType)).Should().Be(0);
    }

    [Fact]
    public async Task CreateRuleAsync_TimeoutActionWithAPositiveDuration_PersistsTheRule()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);

        Result<ModerationRuleDetail> result = await NewService(db)
            .CreateRuleAsync(
                BroadcasterId,
                new()
                {
                    Name = "spam-timeout",
                    Type = "banned_phrase",
                    Action = "timeout",
                    DurationSeconds = 600,
                }
            );

        result.IsSuccess.Should().BeTrue();
        result.Value.DurationSeconds.Should().Be(600);
        result.Value.Action.Should().Be("timeout");
        (await db.Records.CountAsync(r => r.RecordType == RuleRecordType)).Should().Be(1);
    }

    [Fact]
    public async Task CreateRuleAsync_BanActionWithNoDuration_PersistsAsAnExplicitPermanentBanRule()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);

        Result<ModerationRuleDetail> result = await NewService(db)
            .CreateRuleAsync(
                BroadcasterId,
                new()
                {
                    Name = "repeat-offender-ban",
                    Type = "banned_phrase",
                    Action = "ban",
                }
            );

        result.IsSuccess.Should().BeTrue();
        result.Value.Action.Should().Be("ban");
        result.Value.DurationSeconds.Should().BeNull();
    }

    // ─── UpdateRuleAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRuleAsync_ChangingActionToTimeoutWithoutSupplyingADuration_IsRejectedAndLeavesRuleUnchanged()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ModerationService service = NewService(db);

        Result<ModerationRuleDetail> created = await service.CreateRuleAsync(
            BroadcasterId,
            new()
            {
                Name = "repeat-offender-ban",
                Type = "banned_phrase",
                Action = "ban",
            }
        );
        created.IsSuccess.Should().BeTrue();

        // Flip Action to "timeout" WITHOUT also supplying a duration — the merged state (existing
        // DurationSeconds is still null) must still fail the same guard CreateRuleAsync enforces.
        Result<ModerationRuleDetail> updated = await service.UpdateRuleAsync(
            BroadcasterId,
            created.Value.Id,
            new() { Action = "timeout" }
        );

        updated.IsFailure.Should().BeTrue();
        updated.ErrorCode.Should().Be("VALIDATION_FAILED");

        // The persisted rule must still read as the original, unmodified "ban" rule.
        Record record = await db.Records.SingleAsync(r => r.RecordType == RuleRecordType);
        record.Data.Should().Contain("\"Action\":\"ban\"");
    }

    [Fact]
    public async Task UpdateRuleAsync_SettingDurationToZeroOnAnExistingTimeoutRule_IsRejectedAndLeavesRuleUnchanged()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ModerationService service = NewService(db);

        Result<ModerationRuleDetail> created = await service.CreateRuleAsync(
            BroadcasterId,
            new()
            {
                Name = "spam-timeout",
                Type = "banned_phrase",
                Action = "timeout",
                DurationSeconds = 300,
            }
        );
        created.IsSuccess.Should().BeTrue();

        Result<ModerationRuleDetail> updated = await service.UpdateRuleAsync(
            BroadcasterId,
            created.Value.Id,
            new() { DurationSeconds = 0 }
        );

        updated.IsFailure.Should().BeTrue();
        updated.ErrorCode.Should().Be("VALIDATION_FAILED");

        Record record = await db.Records.SingleAsync(r => r.RecordType == RuleRecordType);
        record.Data.Should().Contain("\"DurationSeconds\":300");
    }
}
