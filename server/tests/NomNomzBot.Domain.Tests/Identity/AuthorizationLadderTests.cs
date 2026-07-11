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
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Domain.Tests.Identity;

/// <summary>
/// The authorization ladder is the comparison primitive both gates read (roles-permissions §0): the only
/// thing compared is the numeric level. These tests pin the exact rung values, prove the unified ladder is
/// strictly increasing, and prove the load-bearing cross-plane invariant — a given rung resolves to the same
/// number in every plane (a <c>Moderator</c> is <c>10</c> whether reached via community standing, a
/// management role, or the unified <see cref="PermissionLevel"/>) — so an effective level can be taken as
/// <c>MAX</c> across planes without re-normalising.
/// </summary>
public class AuthorizationLadderTests
{
    [Theory]
    [InlineData(PermissionLevel.Everyone, 0)]
    [InlineData(PermissionLevel.Subscriber, 2)]
    [InlineData(PermissionLevel.Vip, 4)]
    [InlineData(PermissionLevel.Artist, 6)]
    [InlineData(PermissionLevel.Moderator, 10)]
    [InlineData(PermissionLevel.LeadModerator, 20)]
    [InlineData(PermissionLevel.Editor, 30)]
    [InlineData(PermissionLevel.Broadcaster, 40)]
    public void PermissionLevel_maps_to_its_canonical_ladder_value(
        PermissionLevel level,
        int expected
    )
    {
        level.ToLevelValue().Should().Be(expected);
    }

    [Theory]
    [InlineData(0, PermissionLevel.Everyone)]
    [InlineData(1, PermissionLevel.Everyone)] // off-rung → fail closed to the rung actually cleared
    [InlineData(2, PermissionLevel.Subscriber)]
    [InlineData(4, PermissionLevel.Vip)]
    [InlineData(6, PermissionLevel.Artist)]
    [InlineData(10, PermissionLevel.Moderator)]
    [InlineData(15, PermissionLevel.Moderator)]
    [InlineData(20, PermissionLevel.LeadModerator)]
    [InlineData(30, PermissionLevel.Editor)]
    [InlineData(40, PermissionLevel.Broadcaster)]
    [InlineData(99, PermissionLevel.Broadcaster)]
    public void FromLevelValue_maps_to_the_highest_rung_at_or_below(
        int levelValue,
        PermissionLevel expected
    )
    {
        AuthorizationLadder.FromLevelValue(levelValue).Should().Be(expected);
    }

    [Fact]
    public void FromLevelValue_round_trips_every_rung()
    {
        foreach (PermissionLevel level in Enum.GetValues<PermissionLevel>())
            AuthorizationLadder.FromLevelValue(level.ToLevelValue()).Should().Be(level);
    }

    [Fact]
    public void Unified_ladder_is_strictly_increasing_across_all_eight_rungs()
    {
        int[] ladder =
        [
            PermissionLevel.Everyone.ToLevelValue(),
            PermissionLevel.Subscriber.ToLevelValue(),
            PermissionLevel.Vip.ToLevelValue(),
            PermissionLevel.Artist.ToLevelValue(),
            PermissionLevel.Moderator.ToLevelValue(),
            PermissionLevel.LeadModerator.ToLevelValue(),
            PermissionLevel.Editor.ToLevelValue(),
            PermissionLevel.Broadcaster.ToLevelValue(),
        ];

        ladder.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(ManagementRole.Moderator, 10)]
    [InlineData(ManagementRole.LeadModerator, 20)]
    [InlineData(ManagementRole.Editor, 30)]
    [InlineData(ManagementRole.Broadcaster, 40)]
    public void ManagementRole_maps_to_its_plane_B_value(ManagementRole role, int expected)
    {
        role.ToLevel().Should().Be(expected);
    }

    [Theory]
    [InlineData(CommunityStanding.Everyone, 0)]
    [InlineData(CommunityStanding.Subscriber, 2)]
    [InlineData(CommunityStanding.Vip, 4)]
    [InlineData(CommunityStanding.Artist, 6)]
    [InlineData(CommunityStanding.Moderator, 10)]
    public void CommunityStanding_maps_to_its_plane_A_value(
        CommunityStanding standing,
        int expected
    )
    {
        standing.ToLevel().Should().Be(expected);
    }

    [Fact]
    public void Planes_align_on_their_shared_rungs()
    {
        // Moderator is the rung both ladders share — it must be the same number in all three planes.
        PermissionLevel.Moderator.ToLevelValue().Should().Be(10);
        ManagementRole.Moderator.ToLevel().Should().Be(10);
        CommunityStanding.Moderator.ToLevel().Should().Be(10);

        // The low community rungs align with the unified ladder one-for-one.
        CommunityStanding.Everyone.ToLevel().Should().Be(PermissionLevel.Everyone.ToLevelValue());
        CommunityStanding
            .Subscriber.ToLevel()
            .Should()
            .Be(PermissionLevel.Subscriber.ToLevelValue());
        CommunityStanding.Vip.ToLevel().Should().Be(PermissionLevel.Vip.ToLevelValue());
        CommunityStanding.Artist.ToLevel().Should().Be(PermissionLevel.Artist.ToLevelValue());

        // The high management rungs align with the unified ladder one-for-one.
        ManagementRole
            .LeadModerator.ToLevel()
            .Should()
            .Be(PermissionLevel.LeadModerator.ToLevelValue());
        ManagementRole.Editor.ToLevel().Should().Be(PermissionLevel.Editor.ToLevelValue());
        ManagementRole
            .Broadcaster.ToLevel()
            .Should()
            .Be(PermissionLevel.Broadcaster.ToLevelValue());
    }
}
