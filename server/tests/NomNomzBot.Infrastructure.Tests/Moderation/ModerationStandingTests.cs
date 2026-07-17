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
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the bot-side standing writes (J.12): setting persists the tier and refreshes the hot-path
/// cache; re-setting the same identity replaces (never duplicates); the broadcaster themselves is
/// structurally exempt (CONFLICT, no row); every change leaves a system note as the audit trail;
/// clearing hard-deletes the row (absence = normal) and refuses when nothing is set; the user context
/// carries the standings; and Twitch is NEVER called — this axis is bot-side only.
/// </summary>
public sealed class ModerationStandingTests
{
    private const string BroadcasterTwitchId = "1001";
    private const string ViewerTwitchId = "5005";
    private static readonly Guid Tenant = Guid.Parse("019f9a00-8888-7000-8000-000000000001");
    private static readonly Guid Operator = Guid.Parse("019f9a00-8888-7000-8000-000000000009");

    private static async Task<(
        ModerationService Service,
        ModerationServiceTestDbContext Db,
        IChannelRegistry Registry,
        ITwitchModerationApi Twitch
    )> BuildAsync()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Guid.NewGuid(),
                TwitchChannelId = BroadcasterTwitchId,
                ExternalChannelId = BroadcasterTwitchId,
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        await db.SaveChangesAsync();

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        ModerationService service = new(
            db,
            twitch,
            registry,
            TimeProvider.System,
            NullLogger<ModerationService>.Instance,
            Substitute.For<NomNomzBot.Domain.Platform.Interfaces.IEventBus>()
        );
        return (service, db, registry, twitch);
    }

    [Fact]
    public async Task Setting_a_standing_persists_audits_and_refreshes_the_hot_path()
    {
        (
            ModerationService service,
            ModerationServiceTestDbContext db,
            IChannelRegistry registry,
            ITwitchModerationApi twitch
        ) = await BuildAsync();

        Result<ModerationStandingDto> set = await service.SetModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            ViewerTwitchId,
            "twitch",
            ModerationStanding.Muted,
            "spamming links",
            CancellationToken.None
        );

        set.IsSuccess.Should().BeTrue();
        set.Value.Standing.Should().Be("muted");
        ChannelModerationStanding row = await db.ChannelModerationStandings.SingleAsync();
        row.UserId.Should().Be(ViewerTwitchId);
        row.Reason.Should().Be("spamming links");
        await registry
            .Received(1)
            .InvalidateModerationStandingsAsync(Tenant, Arg.Any<CancellationToken>());
        // The audit rides the existing notes surface (Records/user_note).
        (await db.Records.CountAsync(r => r.RecordType == "user_note"))
            .Should()
            .Be(1);
        // Bot-side only: no Helix call of any kind.
        twitch.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Resetting_the_same_identity_replaces_instead_of_duplicating()
    {
        (ModerationService service, ModerationServiceTestDbContext db, _, _) = await BuildAsync();
        await service.SetModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            ViewerTwitchId,
            "twitch",
            ModerationStanding.Muted,
            null,
            CancellationToken.None
        );

        Result<ModerationStandingDto> escalated = await service.SetModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            ViewerTwitchId,
            "twitch",
            ModerationStanding.Blacklisted,
            "escalated",
            CancellationToken.None
        );

        escalated.Value.Standing.Should().Be("blacklisted");
        ChannelModerationStanding row = await db.ChannelModerationStandings.SingleAsync();
        row.Standing.Should().Be("blacklisted");
    }

    [Fact]
    public async Task The_broadcaster_can_never_be_assigned_a_standing()
    {
        (ModerationService service, ModerationServiceTestDbContext db, _, _) = await BuildAsync();

        Result<ModerationStandingDto> set = await service.SetModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            BroadcasterTwitchId,
            "twitch",
            ModerationStanding.Blacklisted,
            null,
            CancellationToken.None
        );

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("CONFLICT");
        (await db.ChannelModerationStandings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_unknown_tier_is_rejected()
    {
        (ModerationService service, _, _, _) = await BuildAsync();

        Result<ModerationStandingDto> set = await service.SetModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            ViewerTwitchId,
            "twitch",
            "grounded",
            null,
            CancellationToken.None
        );

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Clearing_deletes_the_row_and_refuses_when_nothing_is_set()
    {
        (
            ModerationService service,
            ModerationServiceTestDbContext db,
            IChannelRegistry registry,
            _
        ) = await BuildAsync();
        await service.SetModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            ViewerTwitchId,
            "kick",
            ModerationStanding.Shadowbanned,
            null,
            CancellationToken.None
        );
        registry.ClearReceivedCalls();

        Result cleared = await service.ClearModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            ViewerTwitchId,
            "kick",
            CancellationToken.None
        );

        cleared.IsSuccess.Should().BeTrue();
        (await db.ChannelModerationStandings.CountAsync()).Should().Be(0, "absence = normal");
        await registry
            .Received(1)
            .InvalidateModerationStandingsAsync(Tenant, Arg.Any<CancellationToken>());

        Result again = await service.ClearModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            ViewerTwitchId,
            "kick",
            CancellationToken.None
        );
        again.IsFailure.Should().BeTrue();
        again.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task The_user_context_carries_the_standings()
    {
        (ModerationService service, _, _, _) = await BuildAsync();
        await service.SetModerationStandingAsync(
            Tenant.ToString(),
            Operator,
            ViewerTwitchId,
            "twitch",
            ModerationStanding.Muted,
            "chill out",
            CancellationToken.None
        );

        Result<UserModerationContextDto> context = await service.GetUserContextAsync(
            Tenant.ToString(),
            ViewerTwitchId,
            CancellationToken.None
        );

        context.Value.Standings.Should().ContainSingle();
        context.Value.Standings[0].Standing.Should().Be("muted");
        context.Value.Standings[0].Provider.Should().Be("twitch");
    }
}
