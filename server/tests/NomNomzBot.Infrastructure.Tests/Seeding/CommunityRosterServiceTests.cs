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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Community;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Seeding;

/// <summary>
/// Proves the Community moderator-roster Twitch sync (the empty-Community-page fix): it get-or-creates a
/// <c>User</c> per moderator/VIP and upserts the <c>ChannelModerators</c> rows the Community page reads, and is
/// idempotent — a second run over the same Twitch roster adds nothing and creates no duplicate users or rows.
/// </summary>
public sealed class CommunityRosterServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c001");
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

    private static (CommunityRosterService Sut, AuthDbContext Db, ITwitchModeratorsApi Mods) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Guid.Parse("0192a000-0000-7000-8000-00000000c000"),
                TwitchChannelId = "tw-channel",
                Name = "stoney",
                NameNormalized = "stoney",
            }
        );
        db.SaveChanges();

        ITwitchModeratorsApi mods = Substitute.For<ITwitchModeratorsApi>();
        CommunityRosterService sut = new(
            db,
            mods,
            new FakeTimeProvider(Now),
            NullLogger<CommunityRosterService>.Instance
        );
        return (sut, db, mods);
    }

    private static void StubRoster(
        ITwitchModeratorsApi mods,
        IReadOnlyList<TwitchModerator> moderators,
        IReadOnlyList<TwitchVip> vips
    )
    {
        mods.GetModeratorsAsync(Channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new TwitchPage<TwitchModerator>(moderators, null, moderators.Count))
            );
        mods.GetVipsAsync(Channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TwitchPage<TwitchVip>(vips, null, vips.Count)));
    }

    [Fact]
    public async Task Sync_creates_users_and_moderator_rows_from_the_twitch_roster()
    {
        (CommunityRosterService sut, AuthDbContext db, ITwitchModeratorsApi mods) = Build();
        StubRoster(
            mods,
            [
                new TwitchModerator("tw-mod-1", "modone", "ModOne"),
                new TwitchModerator("tw-mod-2", "modtwo", "ModTwo"),
            ],
            [new TwitchVip("tw-vip-1", "VipOne", "vipone")]
        );

        Result<int> result = await sut.SyncModeratorsFromTwitchAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2); // two moderator rows created (VIPs get a User but not a mod row)

        // A User exists for every mod + VIP, with the Twitch login mapped through.
        List<User> users = await db.Users.ToListAsync();
        users
            .Select(u => u.TwitchUserId)
            .Should()
            .BeEquivalentTo(["tw-mod-1", "tw-mod-2", "tw-vip-1"]);
        users.Single(u => u.TwitchUserId == "tw-mod-1").Username.Should().Be("modone");

        // The moderator roster the Community page reads now holds exactly the two moderators (not the VIP).
        List<ChannelModerator> modRows = await db
            .ChannelModerators.Where(m => m.ChannelId == Channel)
            .ToListAsync();
        modRows.Should().HaveCount(2);
        Guid vipUserId = users.Single(u => u.TwitchUserId == "tw-vip-1").Id;
        modRows.Should().NotContain(m => m.UserId == vipUserId);
        modRows.Should().OnlyContain(m => m.GrantedAt == Now.UtcDateTime);
    }

    [Fact]
    public async Task Sync_is_idempotent_a_second_run_adds_nothing()
    {
        (CommunityRosterService sut, AuthDbContext db, ITwitchModeratorsApi mods) = Build();
        StubRoster(mods, [new TwitchModerator("tw-mod-1", "modone", "ModOne")], []);

        Result<int> first = await sut.SyncModeratorsFromTwitchAsync(Channel);
        Result<int> second = await sut.SyncModeratorsFromTwitchAsync(Channel);

        first.Value.Should().Be(1);
        second.Value.Should().Be(0); // already present — nothing new

        (await db.Users.CountAsync(u => u.TwitchUserId == "tw-mod-1")).Should().Be(1);
        (await db.ChannelModerators.CountAsync(m => m.ChannelId == Channel)).Should().Be(1);
    }

    [Fact]
    public async Task Sync_seeds_nothing_when_twitch_returns_an_empty_roster()
    {
        (CommunityRosterService sut, AuthDbContext db, ITwitchModeratorsApi mods) = Build();
        StubRoster(mods, [], []);

        Result<int> result = await sut.SyncModeratorsFromTwitchAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        (await db.ChannelModerators.AnyAsync()).Should().BeFalse();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_fails_for_an_unknown_channel()
    {
        (CommunityRosterService sut, _, ITwitchModeratorsApi mods) = Build();
        StubRoster(mods, [], []);

        Result<int> result = await sut.SyncModeratorsFromTwitchAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Sync_surfaces_the_helix_failure_when_both_reads_fail_instead_of_a_false_zero()
    {
        // The real-world stoney_eagle case: the streamer's token lacks `moderation:read` + `channel:read:vips`,
        // so both Helix reads short-circuit with `missing_scope`. The sync must NOT report a misleading
        // success-with-zero (which renders as a real-looking empty Community page) — it must propagate the
        // failure carrying the scope error code, so the seed handler logs a warning and the dashboard can
        // surface "could not read your moderators" instead of "0 moderators".
        (CommunityRosterService sut, AuthDbContext db, ITwitchModeratorsApi mods) = Build();
        mods.GetModeratorsAsync(Channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TwitchPage<TwitchModerator>>(
                    "Missing required scope 'moderation:read'.",
                    TwitchErrorCodes.MissingScope
                )
            );
        mods.GetVipsAsync(Channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TwitchPage<TwitchVip>>(
                    "Missing required scope 'channel:read:vips'.",
                    TwitchErrorCodes.MissingScope
                )
            );

        Result<int> result = await sut.SyncModeratorsFromTwitchAsync(Channel);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        result.ErrorMessage.Should().Contain("moderation:read");

        // Nothing was written — the channel's real roster is unknown, not empty.
        (await db.ChannelModerators.AnyAsync())
            .Should()
            .BeFalse();
        (await db.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Sync_still_seeds_the_readable_roster_when_only_the_vip_read_fails()
    {
        // A partial outage (VIP read fails, moderator read succeeds) must not throw away the moderators we
        // CAN read: the moderator rows still seed, and the failure is logged (not asserted here) but does
        // not abort the whole sync.
        (CommunityRosterService sut, AuthDbContext db, ITwitchModeratorsApi mods) = Build();
        mods.GetModeratorsAsync(Channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator("tw-mod-1", "modone", "ModOne")],
                        null,
                        1
                    )
                )
            );
        mods.GetVipsAsync(Channel, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TwitchPage<TwitchVip>>(
                    "Twitch request failed (500).",
                    TwitchErrorCodes.TwitchError
                )
            );

        Result<int> result = await sut.SyncModeratorsFromTwitchAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        (await db.ChannelModerators.CountAsync(m => m.ChannelId == Channel)).Should().Be(1);
        (await db.Users.CountAsync(u => u.TwitchUserId == "tw-mod-1")).Should().Be(1);
    }
}
