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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the shared-ban trust web CRUD (moderation.md §3.5, J.9/J.9a): a channel with no row reads as the
/// safe defaults (both OFF, empty list); saves upsert; trust adds are idempotent and refuse self/unknown
/// channels; removes are real deletes with NOT_FOUND on absence; and EVERY write re-verifies the SuperMod
/// floor in-process — a Moderator(10) actor is refused with state untouched even though the HTTP gate passed.
/// </summary>
public sealed class SharedBanServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ba01");
    private static readonly Guid Partner = Guid.Parse("0192a000-0000-7000-8000-00000000ba02");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-00000000ba03");

    private static (SharedBanService Sut, ModerationServiceTestDbContext Db) Build(
        int actorLevel = 20
    )
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .ResolveEffectiveLevelAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(actorLevel));
        return (new SharedBanService(db, roles), db);
    }

    private static async Task SeedChannelsAsync(ModerationServiceTestDbContext db)
    {
        db.Channels.Add(NewChannel(Channel, "trusting_chan"));
        db.Channels.Add(NewChannel(Partner, "partner_chan"));
        await db.SaveChangesAsync();
    }

    private static Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            TwitchChannelId = id.ToString("N")[..8],
            Name = name,
            NameNormalized = name,
        };

    [Fact]
    public async Task A_channel_with_no_row_reads_as_the_safe_defaults()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db) = Build();
        await SeedChannelsAsync(db);

        Result<SharedBanSettingsDto> result = await sut.GetSettingsAsync(Channel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.AcceptSharedChatBans.Should().BeFalse("opt-in, default-deny");
        result.Value.ShareOutgoingBans.Should().BeFalse();
        result.Value.TrustedChannels.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_upserts_the_policy_and_a_second_save_updates_the_same_row()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db) = Build();
        await SeedChannelsAsync(db);

        Result<SharedBanSettingsDto> first = await sut.SaveSettingsAsync(
            Channel,
            Actor,
            new SaveSharedBanSettingsRequest(AcceptSharedChatBans: true, ShareOutgoingBans: false)
        );
        first.Value.AcceptSharedChatBans.Should().BeTrue();

        Result<SharedBanSettingsDto> second = await sut.SaveSettingsAsync(
            Channel,
            Actor,
            new SaveSharedBanSettingsRequest(AcceptSharedChatBans: false, ShareOutgoingBans: true)
        );
        second.Value.AcceptSharedChatBans.Should().BeFalse();
        second.Value.ShareOutgoingBans.Should().BeTrue();

        (await db.SharedBanSettings.CountAsync(s => s.BroadcasterId == Channel))
            .Should()
            .Be(1, "the save is an upsert, never a second row");
    }

    [Fact]
    public async Task Add_trusted_channel_persists_and_readds_idempotently()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db) = Build();
        await SeedChannelsAsync(db);

        Result<SharedBanTrustedChannelDto> added = await sut.AddTrustedChannelAsync(
            Channel,
            Actor,
            Partner
        );
        added.IsSuccess.Should().BeTrue(added.ErrorMessage);
        added.Value.TrustedChannelName.Should().Be("partner_chan");
        added.Value.AddedByUserId.Should().Be(Actor);

        // Re-adding the same partner is a no-op returning the existing row — never a duplicate.
        Result<SharedBanTrustedChannelDto> again = await sut.AddTrustedChannelAsync(
            Channel,
            Actor,
            Partner
        );
        again.IsSuccess.Should().BeTrue(again.ErrorMessage);
        (await db.SharedBanTrustedChannels.CountAsync()).Should().Be(1);

        // The settings read carries the trust list.
        (await sut.GetSettingsAsync(Channel))
            .Value.TrustedChannels.Should()
            .ContainSingle(t => t.TrustedChannelId == Partner);
    }

    [Fact]
    public async Task Add_refuses_self_trust_and_unknown_channels()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db) = Build();
        await SeedChannelsAsync(db);

        (await sut.AddTrustedChannelAsync(Channel, Actor, Channel))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
        (await sut.AddTrustedChannelAsync(Channel, Actor, Guid.NewGuid()))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
    }

    [Fact]
    public async Task Remove_deletes_the_entry_and_absence_is_not_found()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db) = Build();
        await SeedChannelsAsync(db);
        await sut.AddTrustedChannelAsync(Channel, Actor, Partner);

        (await sut.RemoveTrustedChannelAsync(Channel, Actor, Partner)).IsSuccess.Should().BeTrue();
        (await db.SharedBanTrustedChannels.CountAsync()).Should().Be(0);

        (await sut.RemoveTrustedChannelAsync(Channel, Actor, Partner))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
    }

    [Fact]
    public async Task Writes_refuse_an_actor_below_the_supermod_floor_and_touch_nothing()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db) = Build(actorLevel: 10); // Moderator
        await SeedChannelsAsync(db);

        Result<SharedBanSettingsDto> save = await sut.SaveSettingsAsync(
            Channel,
            Actor,
            new SaveSharedBanSettingsRequest(true, true)
        );
        save.ErrorCode.Should().Be("FORBIDDEN");
        (await sut.AddTrustedChannelAsync(Channel, Actor, Partner))
            .ErrorCode.Should()
            .Be("FORBIDDEN");
        (await sut.RemoveTrustedChannelAsync(Channel, Actor, Partner))
            .ErrorCode.Should()
            .Be("FORBIDDEN");

        (await db.SharedBanSettings.CountAsync()).Should().Be(0);
        (await db.SharedBanTrustedChannels.CountAsync()).Should().Be(0);

        // Reads are NOT floor-gated in-service (the read gate is the HTTP policy).
        (await sut.GetSettingsAsync(Channel))
            .IsSuccess.Should()
            .BeTrue();
    }
}
