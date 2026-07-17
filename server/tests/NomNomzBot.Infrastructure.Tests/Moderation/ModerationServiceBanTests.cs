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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;
using Record = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the two behaviours of the ban/timeout fix in <see cref="ModerationService"/>. (1) The broadcaster is
/// structurally un-moderatable: banning or timing out the channel owner's own Twitch id is rejected before any
/// Helix call and writes no local record — Twitch 400s such a request, so a local record would be a fake the
/// dashboard's banned-viewers list then displays. (2) Twitch enforcement happens FIRST: a local
/// <c>moderation_action</c> record is written only after Helix actually applied the ban, so a ban Twitch rejected
/// never shows up as if it had happened. Every assertion is on the resulting persisted state and on whether the
/// Twitch API was actually called — not merely that a call returned.
/// </summary>
public sealed class ModerationServiceBanTests
{
    private const string ActionRecordType = "moderation_action";
    private const string BroadcasterTwitchId = "1001";
    private const string ViewerTwitchId = "5005";

    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8a");

    // The logged-in operator whose OWN Twitch token now signs every dashboard moderation call (moderator_id = them).
    private static readonly Guid Operator = Guid.Parse("019f2802-5c77-7dc8-b6f6-000000000999");
    private static string BroadcasterId => Tenant.ToString();

    private static ModerationService NewService(
        ModerationServiceTestDbContext db,
        ITwitchModerationApi moderation
    ) =>
        new(
            db,
            moderation,
            Substitute.For<NomNomzBot.Domain.Platform.Interfaces.IChannelRegistry>(),
            TimeProvider.System,
            NullLogger<ModerationService>.Instance,
            Substitute.For<IEventBus>()
        );

    /// <summary>A tenant channel whose broadcaster Twitch id is <see cref="BroadcasterTwitchId"/>.</summary>
    private static async Task SeedChannelAsync(ModerationServiceTestDbContext db)
    {
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                TwitchChannelId = BroadcasterTwitchId,
                OwnerUserId = Guid.NewGuid(),
                Name = "c",
                NameNormalized = "c",
            }
        );
        await db.SaveChangesAsync();
    }

    private static Result<TwitchBanResult> TwitchSuccess(string targetTwitchUserId) =>
        Result.Success(
            new TwitchBanResult(
                BroadcasterTwitchId,
                BroadcasterTwitchId,
                targetTwitchUserId,
                DateTimeOffset.UtcNow,
                EndTime: null
            )
        );

    // ─── (1) Broadcaster guard: no Helix call, no record ──────────────────────

    [Fact]
    public async Task BanAsync_TargetingTheBroadcaster_IsRejectedAndNeitherCallsTwitchNorRecords()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();

        // Target id == the channel's own broadcaster Twitch id.
        Result<ModerationActionResult> result = await NewService(db, moderation)
            .BanAsync(BroadcasterId, Operator, BroadcasterTwitchId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CANNOT_MODERATE_BROADCASTER");

        // No fake record was written…
        (await db.Records.CountAsync())
            .Should()
            .Be(0);
        // …and Twitch was never asked to ban the owner (guard short-circuits before the operator Helix call).
        await moderation
            .Received(0)
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TimeoutAsync_TargetingTheBroadcaster_IsRejectedAndNeitherCallsTwitchNorRecords()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();

        Result<ModerationActionResult> result = await NewService(db, moderation)
            .TimeoutAsync(BroadcasterId, Operator, BroadcasterTwitchId, durationSeconds: 600);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CANNOT_MODERATE_BROADCASTER");

        (await db.Records.CountAsync()).Should().Be(0);
        await moderation
            .Received(0)
            .TimeoutAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── (2) Twitch-first success: record written only after Helix applied it ──

    [Fact]
    public async Task BanAsync_WhenTwitchApplies_RecordsExactlyOneActionForTheModLog()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);

        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(TwitchSuccess(ViewerTwitchId));

        Result<ModerationActionResult> result = await NewService(db, moderation)
            .BanAsync(BroadcasterId, Operator, ViewerTwitchId, reason: "spam");

        result.IsSuccess.Should().BeTrue();

        // Twitch was actually asked to ban this viewer AS THE OPERATOR — signed with the operator's own id
        // (moderator_id) against the channel's RAW broadcaster Twitch id, not the tenant Guid.
        await moderation
            .Received(1)
            .BanAsOperatorAsync(
                Operator,
                BroadcasterTwitchId,
                ViewerTwitchId,
                "spam",
                Arg.Any<CancellationToken>()
            );

        // Exactly one action record, of the right type, naming the ban and the target id — this feeds the mod
        // action log. (The dashboard's *banned-users* list is read live from Twitch, not from these rows; that
        // is covered by ModerationServiceTwitchReadsTests.)
        List<Record> records = await db
            .Records.Where(r => r.RecordType == ActionRecordType)
            .ToListAsync();
        records.Should().ContainSingle();
        Record record = records.Single();
        record.BroadcasterId.Should().Be(Tenant);
        record.Data.Should().Contain("ban");
        record.Data.Should().Contain(ViewerTwitchId);
    }

    // ─── (2) No fake record when Twitch rejects the ban ───────────────────────

    [Fact]
    public async Task BanAsync_WhenTwitchRejects_SurfacesTheErrorAndWritesNoRecord()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);

        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchBanResult>("Twitch request failed (400).", "twitch_error")
            );

        Result<ModerationActionResult> result = await NewService(db, moderation)
            .BanAsync(BroadcasterId, Operator, ViewerTwitchId, reason: "spam");

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Be("Twitch request failed (400).");
        result.ErrorCode.Should().Be("twitch_error");

        // Twitch was asked (and refused) — but nothing was recorded, so the banned list stays empty.
        (await db.Records.CountAsync())
            .Should()
            .Be(0);
    }
}
