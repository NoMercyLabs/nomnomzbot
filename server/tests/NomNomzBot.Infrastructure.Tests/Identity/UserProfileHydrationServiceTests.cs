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
/// Proves the viewer-profile hydration mapping (<see cref="UserProfileHydrationService.ApplyProfile"/>) that fills a
/// chatter's Twitch profile so chat + the community page show real avatars/names, not blanks. It writes only fields
/// Twitch actually returned (an ordinary user's empty broadcaster/staff type must not clobber the entity default),
/// sets the immutable account-creation date once, and reports whether anything changed so the worker saves only
/// moved rows.
/// </summary>
public sealed class UserProfileHydrationServiceTests
{
    private static readonly DateTimeOffset CreatedAt = new(2019, 3, 14, 8, 0, 0, TimeSpan.Zero);

    private static TwitchUser Profile(
        string broadcasterType = "affiliate",
        string type = "",
        string description = "just a streamer",
        string avatar = "https://cdn/avatar.png"
    ) =>
        new(
            Id: "42",
            Login: "aaoa_",
            DisplayName: "aaoa_",
            Type: type,
            BroadcasterType: broadcasterType,
            Description: description,
            ProfileImageUrl: avatar,
            OfflineImageUrl: "https://cdn/offline.png",
            ViewCount: 0,
            CreatedAt: CreatedAt
        );

    private static User BareChatter() =>
        new()
        {
            TwitchUserId = "42",
            Username = "aaoa_",
            UsernameNormalized = "aaoa_",
            DisplayName = "aaoa_",
        };

    [Fact]
    public void Hydrates_a_bare_chatter_from_helix()
    {
        User user = BareChatter();

        bool changed = UserProfileHydrationService.ApplyProfile(user, Profile());

        changed.Should().BeTrue();
        user.ProfileImageUrl.Should().Be("https://cdn/avatar.png");
        user.OfflineImageUrl.Should().Be("https://cdn/offline.png");
        user.BroadcasterType.Should().Be("affiliate");
        user.Description.Should().Be("just a streamer");
        user.AccountCreatedAt.Should().Be(CreatedAt.UtcDateTime);
    }

    [Fact]
    public void An_ordinary_users_empty_types_keep_the_entity_default_but_avatar_and_age_fill()
    {
        User user = BareChatter();

        // Twitch returns "" for a non-affiliate / non-staff account — that must not overwrite the "" default with "".
        bool changed = UserProfileHydrationService.ApplyProfile(
            user,
            Profile(broadcasterType: "", type: "", description: "")
        );

        changed.Should().BeTrue("the avatar and account age still fill even for an ordinary user");
        user.ProfileImageUrl.Should().Be("https://cdn/avatar.png");
        user.AccountCreatedAt.Should().Be(CreatedAt.UtcDateTime);
        user.BroadcasterType.Should()
            .Be("", "an empty broadcaster type must not clobber the default");
        user.Type.Should().Be("");
        user.Description.Should().BeNull("an empty description must not overwrite null");
    }

    [Fact]
    public void An_already_hydrated_user_with_identical_values_reports_no_change()
    {
        User user = BareChatter();
        UserProfileHydrationService.ApplyProfile(user, Profile());

        // Re-applying the same Helix record must be a no-op so the worker does not save an unchanged row.
        bool changed = UserProfileHydrationService.ApplyProfile(user, Profile());

        changed.Should().BeFalse();
    }

    [Fact]
    public void Account_created_at_is_set_once_and_never_overwritten()
    {
        User user = BareChatter();
        UserProfileHydrationService.ApplyProfile(user, Profile());
        DateTime? firstSeen = user.AccountCreatedAt;

        // A later hydration reporting a different created_at (should never happen, but be defensive) leaves it fixed.
        TwitchUser drifted = Profile() with
        {
            CreatedAt = CreatedAt.AddYears(1),
        };
        bool changed = UserProfileHydrationService.ApplyProfile(user, drifted);

        changed
            .Should()
            .BeFalse("nothing else changed and the immutable account age is not re-stamped");
        user.AccountCreatedAt.Should().Be(firstSeen);
    }
}
