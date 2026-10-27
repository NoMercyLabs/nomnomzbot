// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>The list and the count of the channel's own Twitch emotes (subscriber, follower and bits tier).</summary>
internal sealed class EmoteChannelHelpers()
    : EmoteHelperGroup(
        EmoteSetRows.List(
            "emote.list.channel",
            "sampleHype sampleLove sampleWave",
            EmoteSources.Channel,
            data => data.ChannelAsync()
        ),
        EmoteSetRows.Count(
            "emote.count.channel",
            "12",
            EmoteSources.Channel,
            data => data.ChannelAsync()
        )
    );

/// <summary>The random pick, the list and the count of the channel's 7TV set.</summary>
internal sealed class EmoteSevenTvHelpers()
    : EmoteHelperGroup(
        EmoteSetRows.Network("7tv", EmoteOrigin.SevenTv, "catJAM", "catJAM KEKW", "48")
    );

/// <summary>The random pick, the list and the count of the channel's BTTV set.</summary>
internal sealed class EmoteBttvHelpers()
    : EmoteHelperGroup(EmoteSetRows.Network("bttv", EmoteOrigin.Bttv, "catJAM", "catJAM", "20"));

/// <summary>The random pick, the list and the count of the channel's FFZ set.</summary>
internal sealed class EmoteFfzHelpers()
    : EmoteHelperGroup(EmoteSetRows.Network("ffz", EmoteOrigin.Ffz, "monkaS", "monkaS", "9"));

/// <summary>Builds the rows every emote set shares: a random pick, the list and the count.</summary>
internal static class EmoteSetRows
{
    public static EmoteRow List(
        string key,
        string sample,
        EmoteSources sources,
        Func<EmoteData, Task<IReadOnlyList<EmoteItem>>> pool
    ) => EmoteRow.Over(key, sample, sources, pool, (emotes, _) => EmoteText.Codes(emotes));

    public static EmoteRow Count(
        string key,
        string sample,
        EmoteSources sources,
        Func<EmoteData, Task<IReadOnlyList<EmoteItem>>> pool
    ) =>
        EmoteRow.Over(
            key,
            sample,
            sources,
            pool,
            (emotes, call) => EmoteText.Count(emotes.Count, call.Culture)
        );

    /// <summary>The <c>random</c>, <c>list</c> and <c>count</c> rows of one third-party network's channel set.</summary>
    public static EmoteRow[] Network(
        string token,
        EmoteOrigin origin,
        string randomSample,
        string listSample,
        string countSample
    )
    {
        Func<EmoteData, Task<IReadOnlyList<EmoteItem>>> pool = async data =>
            [.. (await data.ThirdPartyAsync()).Where(e => e.IsChannelSetOf(origin))];

        return
        [
            EmoteRow.Over(
                "emote.random." + token,
                randomSample,
                EmoteSources.ThirdParty,
                pool,
                EmoteRandomHelpers.PickCode
            ),
            List("emote.list." + token, listSample, EmoteSources.ThirdParty, pool),
            Count("emote.count." + token, countSample, EmoteSources.ThirdParty, pool),
        ];
    }
}
