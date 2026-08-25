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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity.Jobs;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A viewer profile used to be fetched ONCE — the hydration sweep selected only users with no avatar yet —
/// so a viewer who changed their Twitch display name or profile picture kept the old one forever, on the
/// overlay, in chat replies, and everywhere else the dashboard shows them. These hold the two halves of the
/// fix: a re-read actually carries a rename and a new avatar through, and every re-read is stamped so the
/// sweep can tell fresh profiles from stale ones instead of re-fetching the same rows forever.
/// </summary>
public sealed class UserProfileRefreshTests
{
    private static readonly DateTimeOffset CreatedAt = new(2019, 3, 14, 8, 0, 0, TimeSpan.Zero);

    private static TwitchUser Profile(
        string login = "stoney_eagle",
        string displayName = "Stoney_Eagle",
        string avatar = "https://cdn/avatar-v1.png"
    ) =>
        new(
            Id: "42",
            Login: login,
            DisplayName: displayName,
            Type: "",
            BroadcasterType: "affiliate",
            Description: "just a streamer",
            ProfileImageUrl: avatar,
            OfflineImageUrl: "https://cdn/offline.png",
            ViewCount: 0,
            CreatedAt: CreatedAt
        );

    private static User Existing() =>
        new()
        {
            TwitchUserId = "42",
            Username = "stoney_eagle",
            UsernameNormalized = "stoney_eagle",
            DisplayName = "Stoney_Eagle",
            ProfileImageUrl = "https://cdn/avatar-v1.png",
            OfflineImageUrl = "https://cdn/offline.png",
            BroadcasterType = "affiliate",
            Description = "just a streamer",
            AccountCreatedAt = CreatedAt.UtcDateTime,
        };

    [Fact]
    public void A_renamed_viewer_is_carried_across_including_the_lookup_key()
    {
        User user = Existing();

        bool changed = UserProfileHydrationService.ApplyProfile(
            user,
            Profile(login: "stoney_hawk", displayName: "Stoney_Hawk")
        );

        changed.Should().BeTrue();
        user.DisplayName.Should().Be("Stoney_Hawk");
        user.Username.Should().Be("stoney_hawk");
        // The normalized name has to move with it, or every lookup by the new name misses the row and the
        // viewer is silently duplicated as a brand-new user.
        user.UsernameNormalized.Should().Be("stoney_hawk");
    }

    [Fact]
    public void A_new_avatar_replaces_the_old_one_rather_than_being_kept_because_a_value_exists()
    {
        User user = Existing();

        bool changed = UserProfileHydrationService.ApplyProfile(
            user,
            Profile(avatar: "https://cdn/avatar-v2.png")
        );

        changed.Should().BeTrue();
        user.ProfileImageUrl.Should().Be("https://cdn/avatar-v2.png");
    }

    [Fact]
    public void Every_re_read_is_stamped_even_when_nothing_about_the_profile_changed()
    {
        User user = Existing();
        user.ProfileRefreshedAt.Should().BeNull();

        bool changed = UserProfileHydrationService.ApplyProfile(user, Profile());

        // Identical profile — nothing to write...
        changed.Should().BeFalse();
        // ...but it WAS re-read, and without the stamp the sweep would pick this same row again on every
        // tick forever and the backlog would never drain.
        user.ProfileRefreshedAt.Should().NotBeNull();
        user.ProfileRefreshedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void A_later_re_read_moves_the_stamp_forward()
    {
        User user = Existing();
        UserProfileHydrationService.ApplyProfile(user, Profile());
        DateTime first = user.ProfileRefreshedAt!.Value;

        user.ProfileRefreshedAt = first.AddDays(-2);
        UserProfileHydrationService.ApplyProfile(user, Profile());

        user.ProfileRefreshedAt!.Value.Should().BeAfter(first.AddDays(-2));
    }

    [Fact]
    public void A_blank_name_from_the_platform_never_wipes_a_good_one()
    {
        User user = Existing();

        UserProfileHydrationService.ApplyProfile(user, Profile(login: "", displayName: ""));

        // A partial or empty Helix payload must not blank a viewer's identity.
        user.Username.Should().Be("stoney_eagle");
        user.DisplayName.Should().Be("Stoney_Eagle");
    }
}
