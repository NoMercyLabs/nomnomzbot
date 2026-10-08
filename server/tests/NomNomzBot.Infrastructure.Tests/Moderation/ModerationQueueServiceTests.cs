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
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
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
    )> BuildAsync(
        Result? relayResult = null,
        IModerationService? actions = null,
        IEventBus? events = null
    )
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
            actions ?? Substitute.For<IModerationService>(),
            events ?? Substitute.For<IEventBus>(),
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
    public async Task EnqueueFlagAsync_StoresOnePendingFlagWithTheReason_AndNoHeldMessage()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync();

        Result<Guid> result = await service.EnqueueFlagAsync(
            Tenant,
            ModerationQueueSource.HeatThreshold,
            SenderGuid,
            "9001",
            "chatter",
            "Heat reached 85 (threshold 80)."
        );

        result.IsSuccess.Should().BeTrue();
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Id.Should().Be(result.Value);
        stored.Source.Should().Be(ModerationQueueSource.HeatThreshold);
        stored.Status.Should().Be(ModerationQueueStatus.Pending);
        stored.TargetUserId.Should().Be(SenderGuid);
        stored.TargetTwitchUserId.Should().Be("9001");
        stored.TargetUsernameSnapshot.Should().Be("chatter");
        stored.MessageContentSnapshot.Should().Be("Heat reached 85 (threshold 80).");
        stored.AutoModMessageId.Should().BeNull();
    }

    [Fact]
    public async Task EnqueueFlagAsync_WhileTheSameViewerHasAPendingFlag_ReturnsThatRowAndAddsNone()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync();
        Result<Guid> first = await service.EnqueueFlagAsync(
            Tenant,
            ModerationQueueSource.HeatThreshold,
            SenderGuid,
            "9001",
            "chatter",
            "first"
        );

        Result<Guid> second = await service.EnqueueFlagAsync(
            Tenant,
            ModerationQueueSource.HeatThreshold,
            SenderGuid,
            "9001",
            "chatter",
            "second"
        );

        second.Value.Should().Be(first.Value);
        (await db.ModerationQueueItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ResolveAsync_AFlagWithNoHeldMessage_IsResolvedLocally_WithoutCallingHelix()
    {
        (
            ModerationQueueService service,
            ModerationServiceTestDbContext db,
            ITwitchModerationApi moderation
        ) = await BuildAsync();
        Result<Guid> flagged = await service.EnqueueFlagAsync(
            Tenant,
            ModerationQueueSource.HeatThreshold,
            SenderGuid,
            "9001",
            "chatter",
            "Heat reached 85 (threshold 80)."
        );
        Guid moderatorId = Guid.NewGuid();

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            flagged.Value,
            new ResolveModerationQueueItemRequest { Action = "approve" },
            moderatorId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        await moderation
            .DidNotReceiveWithAnyArgs()
            .ManageHeldAutoModMessageAsync(default, default!, default);
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Status.Should().Be(ModerationQueueStatus.Approved);
        stored.ResolvedByUserId.Should().Be(moderatorId);
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

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "approve" },
            moderatorId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Item.Status.Should().Be("approved");
        await moderation
            .Received(1)
            .ManageHeldAutoModMessageAsync(Tenant, "amsg-1", true, Arg.Any<CancellationToken>());

        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Status.Should().Be(ModerationQueueStatus.Approved);
        stored.ResolvedByUserId.Should().Be(moderatorId);
        stored.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_OnAChatFilterItem_RecordsTheVerdictWithoutAskingAutoModToRelease()
    {
        (
            ModerationQueueService service,
            ModerationServiceTestDbContext db,
            ITwitchModerationApi moderation
        ) = await BuildAsync();
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "chat-msg-1",
            "9001",
            "chatter",
            "text",
            "my-filter",
            source: ModerationQueueSource.ChatFilter
        );
        Guid moderatorId = Guid.NewGuid();

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "deny" },
            moderatorId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        await moderation
            .DidNotReceive()
            .ManageHeldAutoModMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Source.Should().Be(ModerationQueueSource.ChatFilter);
        stored.Status.Should().Be(ModerationQueueStatus.Denied);
        stored.ResolvedByUserId.Should().Be(moderatorId);
        stored.ResolutionAction.Should().Be("denied");
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

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "deny" },
            Guid.NewGuid().ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Item.Status.Should().Be("denied");
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

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "approve" },
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
            new ResolveModerationQueueItemRequest { Action = "approve" },
            Guid.NewGuid().ToString()
        );

        Result<ResolveModerationQueueItemResultDto> second = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "deny" },
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

    [Fact]
    public async Task ResolveAsync_DenyWithBanFollowUp_DeniesOnHelixFirst_ThenBansAsTheOperator()
    {
        IModerationService actions = Substitute.For<IModerationService>();
        actions
            .BanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, null)));
        (
            ModerationQueueService service,
            ModerationServiceTestDbContext db,
            ITwitchModerationApi moderation
        ) = await BuildAsync(actions: actions);
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-10",
            "9001",
            "chatter",
            "buy viewers at scam.example",
            "spam"
        );
        Guid moderatorId = Guid.NewGuid();

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest
            {
                Action = "deny",
                FollowUp = "ban",
                Reason = "promo spam",
            },
            moderatorId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.FollowUpError.Should().BeNull();
        result.Value.Item.ResolutionAction.Should().Be("denied_banned");
        Received.InOrder(() =>
        {
            moderation.ManageHeldAutoModMessageAsync(
                Tenant,
                "amsg-10",
                false,
                Arg.Any<CancellationToken>()
            );
            actions.BanAsync(
                BroadcasterId,
                moderatorId,
                "9001",
                "promo spam",
                null,
                Arg.Any<CancellationToken>()
            );
        });
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Status.Should().Be(ModerationQueueStatus.Denied);
        stored.ResolutionAction.Should().Be("denied_banned");
    }

    [Fact]
    public async Task ResolveAsync_DenyWithTimeoutFollowUp_PassesTheDurationThrough()
    {
        IModerationService actions = Substitute.For<IModerationService>();
        actions
            .TimeoutAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, null)));
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync(
            actions: actions
        );
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-11",
            "9001",
            "chatter",
            "text",
            "swearing"
        );
        Guid moderatorId = Guid.NewGuid();

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest
            {
                Action = "deny",
                FollowUp = "timeout",
                TimeoutSeconds = 600,
            },
            moderatorId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.FollowUpError.Should().BeNull();
        await actions
            .Received(1)
            .TimeoutAsync(
                BroadcasterId,
                moderatorId,
                "9001",
                600,
                null,
                null,
                Arg.Any<CancellationToken>()
            );
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.ResolutionAction.Should().Be("denied_timeout");
    }

    [Fact]
    public async Task ResolveAsync_WhenTheFollowUpFails_TheDenyStands_AndTheErrorIsSurfaced()
    {
        IModerationService actions = Substitute.For<IModerationService>();
        actions
            .BanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<ModerationActionResult>(
                    "The operator has no Twitch token.",
                    "TWITCH_ERROR"
                )
            );
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync(
            actions: actions
        );
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-12",
            "9001",
            "chatter",
            "text",
            "spam"
        );

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "deny", FollowUp = "ban" },
            Guid.NewGuid().ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.FollowUpError.Should().Be("The operator has no Twitch token.");
        result.Value.Item.Status.Should().Be("denied");
        result.Value.Item.ResolutionAction.Should().Be("denied");
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Status.Should().Be(ModerationQueueStatus.Denied);
        stored.ResolutionAction.Should().Be("denied");
    }

    [Fact]
    public async Task ResolveAsync_ApproveWithAFollowUp_FailsValidation_BeforeTouchingHelix()
    {
        (ModerationQueueService service, _, ITwitchModerationApi moderation) = await BuildAsync();
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-13",
            "9001",
            "chatter",
            "text",
            "spam"
        );

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "approve", FollowUp = "ban" },
            Guid.NewGuid().ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await moderation
            .DidNotReceiveWithAnyArgs()
            .ManageHeldAutoModMessageAsync(default, default!, default);
    }

    [Fact]
    public async Task ResolveAsync_ATimeoutFollowUpOutOfRange_FailsValidation()
    {
        (ModerationQueueService service, _, _) = await BuildAsync();
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-14",
            "9001",
            "chatter",
            "text",
            "spam"
        );

        foreach (int seconds in new[] { 0, 1209601 })
        {
            Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
                BroadcasterId,
                enqueued.Value,
                new ResolveModerationQueueItemRequest
                {
                    Action = "deny",
                    FollowUp = "timeout",
                    TimeoutSeconds = seconds,
                },
                Guid.NewGuid().ToString()
            );
            result.IsFailure.Should().BeTrue();
            result.ErrorCode.Should().Be("VALIDATION_FAILED");
        }
    }

    [Fact]
    public async Task ResolveAsync_AnUnknownAction_FailsValidation_InsteadOfThrowing()
    {
        (ModerationQueueService service, _, _) = await BuildAsync();
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-15",
            "9001",
            "chatter",
            "text",
            "spam"
        );

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "yeet" },
            Guid.NewGuid().ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task EnqueueHeldMessageAsync_TheSameHoldTwice_ReturnsTheExistingRow()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync();

        Result<Guid> first = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-dup",
            "9001",
            "chatter",
            "text",
            "swearing"
        );
        Result<Guid> second = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-dup",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        second.Value.Should().Be(first.Value);
        (await db.ModerationQueueItems.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueHeldMessageAsync_AChatFilterHoldOfAnAutoModMessage_ReturnsTheExistingRow()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync();
        Result<Guid> autoMod = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-both",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        Result<Guid> chatFilter = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-both",
            "9001",
            "chatter",
            "text",
            "caps",
            source: ModerationQueueSource.ChatFilter
        );

        chatFilter.Value.Should().Be(autoMod.Value);
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Source.Should().Be(ModerationQueueSource.AutoMod);
        stored.AutoModCategory.Should().Be("swearing");
    }

    [Fact]
    public async Task EnqueueHeldMessageAsync_TheSameMessageIdInAnotherChannel_GetsItsOwnRow()
    {
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync();
        Guid otherTenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8b");

        Result<Guid> mine = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-shared",
            "9001",
            "chatter",
            "text",
            "swearing"
        );
        Result<Guid> theirs = await service.EnqueueHeldMessageAsync(
            otherTenant,
            "amsg-shared",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        theirs.Value.Should().NotBe(mine.Value);
        (await db.ModerationQueueItems.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ResolveAsync_WhenTwitchNoLongerHoldsTheMessage_ClosesTheRowAsExpired_AndTellsTheDashboard()
    {
        IEventBus events = Substitute.For<IEventBus>();
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync(
            relayResult: Result.Failure("not found", TwitchErrorCodes.NotFound),
            events: events
        );
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-gone",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "approve" },
            Guid.NewGuid().ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("AUTOMOD_MESSAGE_GONE");
        result.ErrorMessage.Should().Be("Twitch no longer holds this message, so it was closed.");
        ModerationQueueItem stored = await db.ModerationQueueItems.SingleAsync();
        stored.Status.Should().Be(ModerationQueueStatus.Expired);
        stored.ResolutionAction.Should().Be("expired");
        stored.ResolvedAt.Should().NotBeNull();
        stored.ResolvedByUserId.Should().BeNull();
        await events
            .Received(1)
            .PublishAsync(
                Arg.Is<AutoModMessageUpdatedEvent>(e =>
                    e.BroadcasterId == Tenant
                    && e.MessageId == "amsg-gone"
                    && e.UserId == "9001"
                    && e.Status == "expired"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ResolveAsync_WhenTwitchFailsForAnotherReason_LeavesTheRowPending_AndPublishesNothing()
    {
        IEventBus events = Substitute.For<IEventBus>();
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync(
            relayResult: Result.Failure("slow down", TwitchErrorCodes.RateLimited),
            events: events
        );
        Result<Guid> enqueued = await service.EnqueueHeldMessageAsync(
            Tenant,
            "amsg-limited",
            "9001",
            "chatter",
            "text",
            "swearing"
        );

        Result<ResolveModerationQueueItemResultDto> result = await service.ResolveAsync(
            BroadcasterId,
            enqueued.Value,
            new ResolveModerationQueueItemRequest { Action = "deny" },
            Guid.NewGuid().ToString()
        );

        result.ErrorCode.Should().Be(TwitchErrorCodes.RateLimited);
        (await db.ModerationQueueItems.SingleAsync())
            .Status.Should()
            .Be(ModerationQueueStatus.Pending);
        await events
            .DidNotReceive()
            .PublishAsync(Arg.Any<AutoModMessageUpdatedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExpireStaleAutoModAsync_ClosesOnlyOldPendingAutoModRows_AndReportsTheCount()
    {
        IEventBus events = Substitute.For<IEventBus>();
        (ModerationQueueService service, ModerationServiceTestDbContext db, _) = await BuildAsync(
            events: events
        );
        DateTime now = DateTime.UtcNow;
        ModerationQueueItem Row(string messageId, ModerationQueueSource source, TimeSpan age) =>
            new()
            {
                BroadcasterId = Tenant,
                Source = source,
                AutoModMessageId = messageId,
                TargetTwitchUserId = "9001",
                TargetUsernameSnapshot = "chatter",
                CreatedAt = now - age,
            };
        db.ModerationQueueItems.AddRange(
            Row("old-1", ModerationQueueSource.AutoMod, TimeSpan.FromHours(3)),
            Row("old-2", ModerationQueueSource.AutoMod, TimeSpan.FromHours(2)),
            Row("fresh", ModerationQueueSource.AutoMod, TimeSpan.FromMinutes(5)),
            Row("old-filter", ModerationQueueSource.ChatFilter, TimeSpan.FromHours(3))
        );
        await db.SaveChangesAsync();

        int closed = await service.ExpireStaleAutoModAsync(TimeSpan.FromHours(1));

        closed.Should().Be(2);
        Dictionary<string, ModerationQueueItem> byId =
            await db.ModerationQueueItems.ToDictionaryAsync(i => i.AutoModMessageId!);
        byId["old-1"].Status.Should().Be(ModerationQueueStatus.Expired);
        byId["old-1"].ResolutionAction.Should().Be("expired");
        byId["old-1"].ResolvedAt.Should().NotBeNull();
        byId["old-2"].Status.Should().Be(ModerationQueueStatus.Expired);
        byId["fresh"].Status.Should().Be(ModerationQueueStatus.Pending);
        byId["old-filter"].Status.Should().Be(ModerationQueueStatus.Pending);
        await events
            .Received(2)
            .PublishAsync(
                Arg.Is<AutoModMessageUpdatedEvent>(e => e.Status == "expired"),
                Arg.Any<CancellationToken>()
            );
    }
}
