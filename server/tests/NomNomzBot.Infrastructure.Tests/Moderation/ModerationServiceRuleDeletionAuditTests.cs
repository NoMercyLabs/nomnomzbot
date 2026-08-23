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
/// S013c: <see cref="ModerationService.DeleteRuleAsync"/> previously hard-deleted a moderation rule and
/// published only a domain event carrying no actor — mirrors S013 (commit 1e2426e6), which closed the
/// identical gap for <c>CommandService.DeleteAsync</c> by writing a Records audit row naming the actor
/// BEFORE the row is removed. Proven on the persisted audit row, not on "did not throw".
/// </summary>
public sealed class ModerationServiceRuleDeletionAuditTests
{
    private const string RuleRecordType = "moderation_rule";
    private const string RuleAuditRecordType = "moderation_rule_action";
    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8d");
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

    private static async Task<int> CreateRuleAsync(ModerationService service)
    {
        Result<ModerationRuleDetail> created = await service.CreateRuleAsync(
            BroadcasterId,
            new CreateModerationRuleRequest
            {
                Name = "spam-timeout",
                Type = "banned_phrase",
                Action = "timeout",
                DurationSeconds = 300,
            }
        );
        created.IsSuccess.Should().BeTrue();
        return created.Value.Id;
    }

    [Fact]
    public async Task DeleteRuleAsync_WithAnActor_WritesAnAuditRowNamingThatActorBeforeRemoval()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ModerationService service = NewService(db);
        int ruleId = await CreateRuleAsync(service);
        const string actorId = "op-42";

        Result result = await service.DeleteRuleAsync(BroadcasterId, ruleId, actorId);

        result.IsSuccess.Should().BeTrue();

        // The rule row itself is gone (hard delete, as before).
        (await db.Records.CountAsync(r => r.RecordType == RuleRecordType))
            .Should()
            .Be(0);

        // But the deletion is now named to the acting operator, not silently dropped.
        Record audit = await db.Records.SingleAsync(r =>
            r.BroadcasterId == Tenant && r.RecordType == RuleAuditRecordType
        );
        audit.UserId.Should().Be(actorId);
        audit.Data.Should().Contain("\"Action\":\"rule_deleted\"");
        audit.Data.Should().Contain("\"Subject\":\"spam-timeout\"");
    }

    [Fact]
    public async Task DeleteRuleAsync_WithNoActorSupplied_StillRecordsTheDeletionRatherThanDroppingAttribution()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ModerationService service = NewService(db);
        int ruleId = await CreateRuleAsync(service);

        Result result = await service.DeleteRuleAsync(BroadcasterId, ruleId);

        result.IsSuccess.Should().BeTrue();

        Record audit = await db.Records.SingleAsync(r =>
            r.BroadcasterId == Tenant && r.RecordType == RuleAuditRecordType
        );
        // Falls back to the tenant id (same convention as CommandService.DeleteAsync / RecordActionAsync)
        // rather than a null/empty UserId that would make the row unattributable.
        audit.UserId.Should().Be(Tenant.ToString());
    }

    [Fact]
    public async Task DeleteRuleAsync_RuleNotFound_WritesNoAuditRow()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ModerationService service = NewService(db);

        Result result = await service.DeleteRuleAsync(BroadcasterId, 999, "op-42");

        result.IsFailure.Should().BeTrue();
        (await db.Records.CountAsync(r => r.RecordType == RuleAuditRecordType)).Should().Be(0);
    }
}
