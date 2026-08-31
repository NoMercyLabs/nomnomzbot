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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the AutoMod review queue (moderation.md J.1, S066 done-when: "a mod approves a held message from the
/// dashboard"): a hold enqueues a pending row with the sender resolved as a real user, a moderator lists the
/// pending queue, resolving it relays through Helix and only THEN flips the local status — a Helix failure leaves
/// the row pending so the moderator can retry — and an external Twitch-reported resolution closes a row with no
/// resolver attributed.
/// </summary>
public sealed class ModerationQueueServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8a");
    private static string BroadcasterId => Tenant.ToString();
    private static readonly Guid SenderGuid = Guid.Parse("019f2900-0000-7000-8000-000000000002");

    private static async Task<(
        ModerationQueueService Service,
        ModerationServiceTestDbContext Db,
        ITwitchModerationApi Moderation
    )> BuildAsync(Result? relayResult = null)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
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

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        SenderGuid.ToString(),
                        "chatter",
                        "Chatter",
                        null,
                        null,
                        default,
                        default
                    )
                )
            );

        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .ManageHeldAutoModMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(relayResult ?? Result.Success());

        ModerationQueueService service = new(
            db,
            users,
            moderation,
            TimeProvider.System,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<ModerationQueueService>>()
        );
        return (service, db, moderation);
    }

    [Fact]
    public async Task EnqueueHeldMessageAsync_ResolvesTheSender_AndStoresAPendingRow()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync();

        Result<Guid> result = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-1",
            "9001",
            "chatter",
            "you are all idiots",
            "aggression"
        );

        result.IsSuccess.Should().BeTrue();
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.BroadcasterId.Should().Be(Tenant);
        stored.Source.Should().Be(ModerationQueueSource.AutoMod);
        stored.Status.Should().Be(ModerationQueueStatus.Pending);
        stored.TargetUserId.Should().Be(SenderGuid);
        stored.TargetTwitchUserId.Should().Be("9001");
        stored.AutoModMessageId.Should().Be("amsg-1");
        stored.AutoModCategory.Should().Be("aggression");
    }

    [Fact]
    public async Task ListAsync_FiltersByStatus_AndRejectsAnUnknownStatus()
    {
        (ModerationQueueService service, _, _) = await BuildAsync();
        await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-1",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        Result<List<ModerationQueueItemDto>> pending = await service.ListAsync(
            BroadcasterId,
            "pending"
        );
        pending.IsSuccess.Should().BeTrue();
        pending.Value.Should().ContainSingle();

        Result<List<ModerationQueueItemDto>> approved = await service.ListAsync(
            BroadcasterId,
            "approved"
        );
        approved.Value.Should().BeEmpty();

        Result<List<ModerationQueueItemDto>> bad = await service.ListAsync(BroadcasterId, "bogus");
        bad.IsFailure.Should().BeTrue();
        bad.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task ResolveAsync_Approve_RelaysThroughHelix_ThenFlipsTheRowAndRecordsTheModerator()
    {
        (
            ModerationQueueService service,
            ModerationServiceTestDbContext db,
            ITwitchModerationApi moderation
        ) = await BuildAsync();
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-1",
            "9001",
            "chatter",
            "text",
            "swearing"
        );
        Guid moderatorId = Guid.NewGuid();

        Result<ModerationQueueItemDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            "approve",
            moderatorId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("approved");
        await moderation
            .Received(1)
            .ManageHeldAutoModMessageAsync(Tenant, "amsg-1", true, Arg.Any<CancellationToken>());

        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Status.Should().Be(ModerationQueueStatus.Approved);
        stored.ResolvedByUserId.Should().Be(moderatorId);
        stored.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_ADeny_CallsHelixWithApproveFalse()
    {
        (ModerationQueueService service, _, ITwitchModerationApi moderation) = await BuildAsync();
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-2",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        Result<ModerationQueueItemDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            "deny",
            Guid.NewGuid().ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("denied");
        await moderation
            .Received(1)
            .ManageHeldAutoModMessageAsync(Tenant, "amsg-2", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_WhenHelixFails_LeavesTheRowPending()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync(
            relayResult: Result.Failure("Twitch rejected it.", "TWITCH_ERROR")
        );
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-3",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        Result<ModerationQueueItemDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            "approve",
            Guid.NewGuid().ToString()
        );

        result.IsFailure.Should().BeTrue();
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Status.Should().Be(ModerationQueueStatus.Pending);
        stored.ResolvedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ASecondTime_FailsWithoutCallingHelixAgain()
    {
        (ModerationQueueService service, _, ITwitchModerationApi moderation) = await BuildAsync();
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-4",
            "9001",
            "chatter",
            "text",
            "swearing"
        );
        await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            "approve",
            Guid.NewGuid().ToString()
        );

        Result<ModerationQueueItemDto> second = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            "deny",
            Guid.NewGuid().ToString()
        );

        second.IsFailure.Should().BeTrue();
        await moderation
            .Received(1)
            .ManageHeldAutoModMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ApplyExternalResolutionAsync_ClosesTheRow_WithNoResolverAttributed()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync();
        await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-5",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        await service.ApplyExternalResolutionAsync(Tenant, "amsg-5", "denied");

        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Status.Should().Be(ModerationQueueStatus.Denied);
        stored.ResolvedByUserId.Should().BeNull();
        stored.ResolutionAction.Should().Be("denied");
    }

    [Fact]
    public async Task ApplyExternalResolutionAsync_ForAnUnknownMessage_IsANoOp()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync();

        await service.ApplyExternalResolutionAsync(Tenant, "no-such-message", "approved");

        (await db.ModerationQueueItems.CountAsync()).Should().Be(0);
    }
}
