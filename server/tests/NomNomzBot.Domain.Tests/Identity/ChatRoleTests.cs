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
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Domain.Tests.Identity;

/// <summary>
/// Proves the chat role resolution (roles-permissions §0): a Lead Moderator (Twitch's <c>lead_moderator</c> badge,
/// which replaces the regular moderator badge) resolves to SuperMod — above a plain moderator — whether the marker is
/// carried as the badge set_id or its version id, and even if the moderator flag itself is absent.
/// </summary>
public sealed class ChatRoleTests
{
    private static ChatBadge Badge(string setId, string id) => new(setId, id);

    [Fact]
    public void Lead_moderator_badge_resolves_to_super_mod_via_set_id()
    {
        ChatRole
            .Resolve(false, true, false, false, [Badge("lead_moderator", "1")])
            .Should()
            .Be(PermissionLevel.SuperMod);
    }

    [Fact]
    public void Lead_moderator_badge_resolves_to_super_mod_via_version_id()
    {
        // Twitch may carry the marker as the badge version id while set_id stays "moderator".
        ChatRole
            .Resolve(false, true, false, false, [Badge("moderator", "lead_moderator")])
            .Should()
            .Be(PermissionLevel.SuperMod);
    }

    [Fact]
    public void A_lead_mod_resolves_to_super_mod_even_when_the_moderator_flag_is_absent()
    {
        // Covers the badge fully replacing the moderator badge: the flag can be false, the badge still promotes.
        PermissionLevel level = ChatRole.Resolve(
            false,
            isModerator: false,
            false,
            false,
            [Badge("lead_moderator", "1")]
        );

        level.Should().Be(PermissionLevel.SuperMod);
        level.ToLevelValue().Should().BeGreaterThan(PermissionLevel.Moderator.ToLevelValue());
    }

    [Fact]
    public void Broadcaster_outranks_a_lead_mod_badge()
    {
        ChatRole
            .Resolve(true, false, false, false, [Badge("lead_moderator", "1")])
            .Should()
            .Be(PermissionLevel.Broadcaster);
    }

    [Theory]
    [InlineData(false, false, false, false, PermissionLevel.Everyone)]
    [InlineData(false, false, false, true, PermissionLevel.Subscriber)]
    [InlineData(false, false, true, false, PermissionLevel.Vip)]
    [InlineData(false, true, false, false, PermissionLevel.Moderator)]
    public void Flags_resolve_to_their_level_without_a_lead_badge(
        bool broadcaster,
        bool moderator,
        bool vip,
        bool subscriber,
        PermissionLevel expected
    )
    {
        ChatRole.Resolve(broadcaster, moderator, vip, subscriber, []).Should().Be(expected);
    }

    [Fact]
    public void A_lead_mod_passes_a_moderator_gate_but_a_moderator_fails_a_super_mod_gate()
    {
        int leadMod = PermissionLevel.SuperMod.ToLevelValue();
        int moderator = PermissionLevel.Moderator.ToLevelValue();

        (leadMod >= ChatRole.Parse("moderator").ToLevelValue()).Should().BeTrue();
        (leadMod >= ChatRole.Parse("supermod").ToLevelValue()).Should().BeTrue();
        (moderator >= ChatRole.Parse("supermod").ToLevelValue()).Should().BeFalse();
    }

    [Theory]
    [InlineData("supermod", PermissionLevel.SuperMod)]
    [InlineData("lead_moderator", PermissionLevel.SuperMod)]
    [InlineData("mod", PermissionLevel.Moderator)]
    [InlineData("editor", PermissionLevel.Editor)]
    [InlineData("broadcaster", PermissionLevel.Broadcaster)]
    [InlineData("", PermissionLevel.Everyone)]
    [InlineData(null, PermissionLevel.Everyone)]
    public void Parse_maps_tokens_to_their_ladder_rung(string? token, PermissionLevel expected)
    {
        ChatRole.Parse(token).Should().Be(expected);
    }
}
