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
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Decoration;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the decided emote-matching rules live in the index (chat-decoration spec §9·5/§9·6): case-insensitive exact
/// match on the code, and on a collision the set passed earlier wins — which is how channel-over-global and the fixed
/// cross-provider order (7TV→BTTV→FFZ) are resolved by the order the caller assembles the sets.
/// </summary>
public sealed class ChannelEmoteIndexTests
{
    private static ChatEmote Emote(EmoteProvider provider, string code, string id) =>
        new(
            provider,
            id,
            code,
            new Dictionary<string, string> { ["1"] = $"https://cdn/{id}/1x" },
            Animated: false,
            ZeroWidth: false
        );

    [Fact]
    public void Match_is_case_insensitive_on_the_code()
    {
        ChatEmote kappa = Emote(EmoteProvider.SevenTv, "Kappa", "a");
        ChannelEmoteIndex index = ChannelEmoteIndex.Build([
            [kappa],
        ]);

        index.TryMatch("kappa", out ChatEmote? lower).Should().BeTrue();
        lower.Should().Be(kappa);
        index.TryMatch("KAPPA", out ChatEmote? upper).Should().BeTrue();
        upper.Should().Be(kappa);
    }

    [Fact]
    public void Earlier_set_wins_a_code_collision_so_channel_beats_global()
    {
        ChatEmote channel = Emote(EmoteProvider.Bttv, "LUL", "channel");
        ChatEmote global = Emote(EmoteProvider.Bttv, "LUL", "global");

        // channel set is passed before the global set (its precedence position).
        ChannelEmoteIndex index = ChannelEmoteIndex.Build([
            [channel],
            [global],
        ]);

        index.TryMatch("LUL", out ChatEmote? winner).Should().BeTrue();
        winner!.Id.Should().Be("channel");
    }

    [Fact]
    public void First_provider_in_order_claims_a_shared_code()
    {
        ChatEmote sevenTv = Emote(EmoteProvider.SevenTv, "Pog", "7tv");
        ChatEmote bttv = Emote(EmoteProvider.Bttv, "Pog", "bttv");

        // 7TV precedes BTTV in the assembled order (spec §9·6).
        ChannelEmoteIndex index = ChannelEmoteIndex.Build([
            [sevenTv],
            [bttv],
        ]);

        index.TryMatch("Pog", out ChatEmote? winner).Should().BeTrue();
        winner!.Provider.Should().Be(EmoteProvider.SevenTv);
    }

    [Fact]
    public void A_non_emote_word_does_not_match()
    {
        ChannelEmoteIndex index = ChannelEmoteIndex.Build([
            [Emote(EmoteProvider.Ffz, "ZreknarF", "z")],
        ]);

        index.TryMatch("hello", out ChatEmote? emote).Should().BeFalse();
        emote.Should().BeNull();
    }
}
