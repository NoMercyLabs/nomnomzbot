// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves every shared-ban setting has ONE default (<see cref="SharedBanDefaults"/>) read by the entity and
/// the service; that a read of a channel with no row resolves to those defaults and writes nothing; and that
/// each field survives PUT then GET with a value that differs from the default.
/// </summary>
public sealed class SharedBanDefaultsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000bd01");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-00000000bd03");

    private static (SharedBanService Sut, ModerationServiceTestDbContext Db) Build()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .ResolveEffectiveLevelAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(20));
        return (
            new(
                db,
                roles,
                new SharedChatSessionTracker(),
                Substitute.For<ITwitchModerationApi>(),
                new RecordingEventBus()
            ),
            db
        );
    }

    [Fact]
    public async Task A_channel_with_no_row_reads_the_defaults_and_the_read_writes_nothing()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db) = Build();

        Result<SharedBanSettingsDto> read = await sut.GetSettingsAsync(Channel);

        read.IsSuccess.Should().BeTrue();
        read.Value.AcceptSharedChatBans.Should().Be(SharedBanDefaults.AcceptSharedChatBans);
        read.Value.ShareOutgoingBans.Should().Be(SharedBanDefaults.ShareOutgoingBans);
        read.Value.TrustedChannels.Should().BeEmpty();
        (await db.SharedBanSettings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void The_entity_starts_from_the_one_default_and_the_defaults_are_opt_in_off()
    {
        SharedBanSettings entity = new();

        entity.AcceptSharedChatBans.Should().Be(SharedBanDefaults.AcceptSharedChatBans);
        entity.ShareOutgoingBans.Should().Be(SharedBanDefaults.ShareOutgoingBans);
        SharedBanDefaults.AcceptSharedChatBans.Should().BeFalse();
        SharedBanDefaults.ShareOutgoingBans.Should().BeFalse();
    }

    [Fact]
    public async Task Every_field_round_trips_put_then_get_with_non_default_values()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db) = Build();
        bool accept = !SharedBanDefaults.AcceptSharedChatBans;
        bool share = !SharedBanDefaults.ShareOutgoingBans;

        Result<SharedBanSettingsDto> save = await sut.SaveSettingsAsync(
            Channel,
            Actor,
            new(accept, share)
        );
        SharedBanSettingsDto read = (await sut.GetSettingsAsync(Channel)).Value;

        save.IsSuccess.Should().BeTrue();
        read.AcceptSharedChatBans.Should().Be(accept);
        read.ShareOutgoingBans.Should().Be(share);
        SharedBanSettings row = await db.SharedBanSettings.SingleAsync();
        row.AcceptSharedChatBans.Should().Be(accept);
        row.ShareOutgoingBans.Should().Be(share);
    }
}
