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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves S066-mod-actions: adding/removing a moderator and clearing chat each actually call the
/// corresponding Helix method with the tenant's own broadcaster id and the given target — not merely that
/// the service call "succeeded". Add/Remove Channel Moderator requires the broadcaster's OWN token (not
/// delegable to an operator's), so these always resolve to the tenant Guid, mirroring
/// <c>GetBannedUsersAsync</c>'s existing broadcaster-token pattern.
/// </summary>
public sealed class ModerationServiceModeratorTests
{
    private const string BroadcasterTwitchId = "1001";
    private const string ViewerTwitchId = "5005";

    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8c");
    private static string BroadcasterId => Tenant.ToString();

    private static ModerationService NewService(
        ModerationServiceTestDbContext db,
        ITwitchModerationApi moderation,
        ITwitchModeratorsApi moderators
    ) =>
        new(
            db,
            moderation,
            moderators,
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
                TwitchChannelId = BroadcasterTwitchId,
                OwnerUserId = Guid.NewGuid(),
                Name = "c",
                NameNormalized = "c",
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AddModeratorAsync_CallsHelixWithTenantIdAndTargetTwitchId_AndSucceeds()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(Tenant, ViewerTwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result result = await NewService(db, Substitute.For<ITwitchModerationApi>(), moderators)
            .AddModeratorAsync(BroadcasterId, ViewerTwitchId);

        result.IsSuccess.Should().BeTrue();
        await moderators
            .Received(1)
            .AddModeratorAsync(Tenant, ViewerTwitchId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddModeratorAsync_WhenHelixFails_PropagatesTheFailureUnchanged()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .AddModeratorAsync(Tenant, ViewerTwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Twitch rejected it.", "TWITCH_ERROR"));

        Result result = await NewService(db, Substitute.For<ITwitchModerationApi>(), moderators)
            .AddModeratorAsync(BroadcasterId, ViewerTwitchId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TWITCH_ERROR");
    }

    [Fact]
    public async Task RemoveModeratorAsync_CallsHelixWithTenantIdAndTargetTwitchId_AndSucceeds()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .RemoveModeratorAsync(Tenant, ViewerTwitchId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result result = await NewService(db, Substitute.For<ITwitchModerationApi>(), moderators)
            .RemoveModeratorAsync(BroadcasterId, ViewerTwitchId);

        result.IsSuccess.Should().BeTrue();
        await moderators
            .Received(1)
            .RemoveModeratorAsync(Tenant, ViewerTwitchId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearChatAsync_CallsDeleteAllChatMessagesWithTenantId_AndSucceeds()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .DeleteAllChatMessagesAsync(Tenant, Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result result = await NewService(db, moderation, Substitute.For<ITwitchModeratorsApi>())
            .ClearChatAsync(BroadcasterId);

        result.IsSuccess.Should().BeTrue();
        await moderation
            .Received(1)
            .DeleteAllChatMessagesAsync(Tenant, Arg.Any<CancellationToken>());
        // Clear Chat omits message_id entirely (it is NOT DeleteChatMessageAsync for a specific message).
        await moderation
            .DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ClearChatAsync_WhenHelixFails_PropagatesTheFailureUnchanged()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .DeleteAllChatMessagesAsync(Tenant, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Twitch rejected it.", "TWITCH_ERROR"));

        Result result = await NewService(db, moderation, Substitute.For<ITwitchModeratorsApi>())
            .ClearChatAsync(BroadcasterId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TWITCH_ERROR");
    }

    [Fact]
    public async Task GetModeratorsAsync_ReturnsTheModeratorsFromTheHelixPage()
    {
        await using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedChannelAsync(db);
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .GetModeratorsAsync(Tenant, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator(ViewerTwitchId, "viewerlogin", "ViewerName")],
                        NextCursor: null,
                        Total: 1
                    )
                )
            );

        Result<List<ModeratorDto>> result = await NewService(
                db,
                Substitute.For<ITwitchModerationApi>(),
                moderators
            )
            .GetModeratorsAsync(BroadcasterId);

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Should()
            .ContainSingle(m => m.UserId == ViewerTwitchId && m.Username == "ViewerName");
    }
}
