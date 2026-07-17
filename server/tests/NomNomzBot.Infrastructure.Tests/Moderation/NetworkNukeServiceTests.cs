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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using RecordEntity = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the SuperMod platform nuke (moderation.md §3.4, J.2a): the fan-out bans on each channel's OWN
/// token and only where the ACTOR holds SuperMod+ (per-channel in-service re-check); every successful leg
/// is a provenance record carrying the batch id; a failed leg marks the batch partial without blocking the
/// rest; the one-shot revert unbans exactly the recorded legs; and the confirmation + floor guardrails
/// refuse with nothing persisted.
/// </summary>
public sealed class NetworkNukeServiceTests
{
    private static readonly Guid Origin = Guid.Parse("0192a000-0000-7000-8000-00000000cc01");
    private static readonly Guid OtherMine = Guid.Parse("0192a000-0000-7000-8000-00000000cc02");
    private static readonly Guid NotMine = Guid.Parse("0192a000-0000-7000-8000-00000000cc03");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-00000000cc04");
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 5, 0, 0, TimeSpan.Zero);

    private static (
        NetworkNukeService Sut,
        ModerationServiceTestDbContext Db,
        ITwitchModerationApi Twitch,
        RecordingEventBus Bus
    ) Build()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        // The actor is SuperMod on Origin + OtherMine, only Moderator on NotMine.
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .ResolveEffectiveLevelAsync(Actor, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(call.ArgAt<Guid>(1) == NotMine ? 10 : 20));
        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        twitch
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchBanResult("b", "b", "troll-42", DateTimeOffset.UnixEpoch, null)
                )
            );
        twitch
            .UnbanUserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        RecordingEventBus bus = new();
        NetworkNukeService sut = new(
            db,
            roles,
            twitch,
            bus,
            new FakeTimeProvider(Now),
            NullLogger<NetworkNukeService>.Instance
        );
        return (sut, db, twitch, bus);
    }

    private static async Task SeedChannelsAsync(ModerationServiceTestDbContext db)
    {
        foreach (
            (Guid id, string name) in new[]
            {
                (Origin, "origin_chan"),
                (OtherMine, "other_mine"),
                (NotMine, "not_mine"),
            }
        )
            db.Channels.Add(
                new Channel
                {
                    Id = id,
                    OwnerUserId = Guid.NewGuid(),
                    TwitchChannelId = name,
                    Name = name,
                    NameNormalized = name,
                    IsOnboarded = true,
                }
            );
        await db.SaveChangesAsync();
    }

    private static NetworkNukeRequest Req(bool confirm = true) =>
        new()
        {
            TargetTwitchUserId = "troll-42",
            Reason = "bot raid",
            RequireConfirmation = confirm,
        };

    [Fact]
    public async Task Nuke_bans_only_the_actors_supermod_channels_and_records_each_leg()
    {
        (
            NetworkNukeService sut,
            ModerationServiceTestDbContext db,
            ITwitchModerationApi twitch,
            RecordingEventBus bus
        ) = Build();
        await SeedChannelsAsync(db);

        Result<NetworkNukeBatchDto> result = await sut.NukeAsync(Origin, Actor, Req());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.ChannelCount.Should().Be(2, "the actor is SuperMod on 2 of the 3 channels");
        result.Value.Status.Should().Be("active");

        // Both SuperMod channels were banned on their OWN tokens; the Moderator-only one never was.
        await twitch
            .Received(1)
            .BanUserAsync(Origin, "troll-42", "bot raid", Arg.Any<CancellationToken>());
        await twitch
            .Received(1)
            .BanUserAsync(OtherMine, "troll-42", "bot raid", Arg.Any<CancellationToken>());
        await twitch
            .DidNotReceive()
            .BanUserAsync(
                NotMine,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );

        // One provenance record per leg, carrying the batch id + network_nuke origin.
        List<RecordEntity> legs = await db
            .Records.Where(r => r.RecordType == "moderation_action")
            .ToListAsync();
        legs.Should().HaveCount(2);
        legs.Select(l => l.BroadcasterId).Should().BeEquivalentTo([Origin, OtherMine]);
        legs.Should()
            .OnlyContain(l =>
                l.Data.Contains(result.Value.Id.ToString())
                && l.Data.Contains("\"Origin\":\"network_nuke\"")
            );

        bus.Published.OfType<NetworkNukeExecutedEvent>().Single().ChannelCount.Should().Be(2);
    }

    [Fact]
    public async Task A_failed_leg_marks_the_batch_partial_and_records_nothing_for_it()
    {
        (
            NetworkNukeService sut,
            ModerationServiceTestDbContext db,
            ITwitchModerationApi twitch,
            _
        ) = Build();
        await SeedChannelsAsync(db);
        twitch
            .BanUserAsync(
                OtherMine,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchBanResult>("dead token", "TOKEN_EXPIRED"));

        Result<NetworkNukeBatchDto> result = await sut.NukeAsync(Origin, Actor, Req());

        result.Value.Status.Should().Be("partial");
        result.Value.ChannelCount.Should().Be(1, "only the origin leg succeeded");
        (await db.Records.CountAsync(r => r.BroadcasterId == OtherMine)).Should().Be(0);
    }

    [Fact]
    public async Task Revert_unbans_exactly_the_recorded_legs_and_double_revert_is_refused()
    {
        (
            NetworkNukeService sut,
            ModerationServiceTestDbContext db,
            ITwitchModerationApi twitch,
            _
        ) = Build();
        await SeedChannelsAsync(db);
        Result<NetworkNukeBatchDto> nuked = await sut.NukeAsync(Origin, Actor, Req());

        Result<NetworkNukeBatchDto> reverted = await sut.RevertAsync(Actor, nuked.Value.Id);

        reverted.IsSuccess.Should().BeTrue(reverted.ErrorMessage);
        reverted.Value.Status.Should().Be("reverted");
        reverted.Value.RevertedByUserId.Should().Be(Actor);
        reverted.Value.RevertedAt.Should().Be(Now.UtcDateTime);
        await twitch.Received(1).UnbanUserAsync(Origin, "troll-42", Arg.Any<CancellationToken>());
        await twitch
            .Received(1)
            .UnbanUserAsync(OtherMine, "troll-42", Arg.Any<CancellationToken>());
        await twitch
            .DidNotReceive()
            .UnbanUserAsync(NotMine, Arg.Any<string>(), Arg.Any<CancellationToken>());

        (await sut.RevertAsync(Actor, nuked.Value.Id)).ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Guardrails_refuse_without_confirmation_or_below_the_floor()
    {
        (
            NetworkNukeService sut,
            ModerationServiceTestDbContext db,
            ITwitchModerationApi twitch,
            _
        ) = Build();
        await SeedChannelsAsync(db);

        (await sut.NukeAsync(Origin, Actor, Req(confirm: false)))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
        // Initiated FROM a channel where the actor is only Moderator → refused outright.
        (await sut.NukeAsync(NotMine, Actor, Req()))
            .ErrorCode.Should()
            .Be("FORBIDDEN");

        (await db.NetworkNukeBatches.CountAsync())
            .Should()
            .Be(0, "no guardrail miss persists a batch");
        await twitch
            .DidNotReceive()
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ListBatches_is_scoped_to_the_origin_channel_newest_first()
    {
        (NetworkNukeService sut, ModerationServiceTestDbContext db, _, _) = Build();
        await SeedChannelsAsync(db);
        await sut.NukeAsync(Origin, Actor, Req());
        await sut.NukeAsync(OtherMine, Actor, Req());

        Result<PagedList<NetworkNukeBatchDto>> list = await sut.ListBatchesAsync(
            Origin,
            new PaginationParams(1, 10, null, null)
        );

        list.Value.Items.Should().ContainSingle();
        list.Value.Items[0].OriginBroadcasterId.Should().Be(Origin);
    }
}
