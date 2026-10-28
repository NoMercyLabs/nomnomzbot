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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>One random emote of the channel's Twitch set, of Twitch's globals, or of every network.</summary>
internal sealed class EmoteRandomHelpers()
    : EmoteHelperGroup(
        ChannelPick("emote.random", "sampleHype", EmoteSources.Channel, _ => true),
        ChannelPick(
            "emote.random.sub",
            "sampleLove",
            EmoteSources.Channel,
            e => e.Type == EmoteItem.SubscriberType
        ),
        ChannelPick(
            "emote.random.follower",
            "sampleWave",
            EmoteSources.Channel,
            e => e.Type == EmoteItem.FollowerType
        ),
        ChannelPick(
            "emote.random.bits",
            "sampleBits",
            EmoteSources.Channel,
            e => e.Type == EmoteItem.BitsType
        ),
        EmoteRow.Over(
            "emote.random.global",
            "Kappa",
            EmoteSources.Global,
            data => data.GlobalAsync(),
            PickCode
        ),
        EmoteRow.Over(
            "emote.random.any",
            "catJAM",
            EmoteSources.Catalogue,
            data => data.AllAsync(),
            PickCode
        )
    )
{
    /// <summary>One random emote code of the pool; empty when the pool is empty.</summary>
    internal static string PickCode(IReadOnlyList<EmoteItem> pool, EmoteCall call) =>
        pool.Count == 0 ? string.Empty : call.Pick(pool).Code;

    /// <summary>A random pick among the channel's own Twitch emotes that pass the filter.</summary>
    private static EmoteRow ChannelPick(
        string key,
        string sample,
        EmoteSources sources,
        Func<EmoteItem, bool> keep
    ) =>
        EmoteRow.Over(
            key,
            sample,
            sources,
            async data => [.. (await data.ChannelAsync()).Where(keep)],
            PickCode
        );
}
