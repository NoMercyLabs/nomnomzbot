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

/// <summary>The network an emote of the channel catalogue comes from.</summary>
internal enum EmoteOrigin
{
    TwitchChannel,
    TwitchGlobal,
    SevenTv,
    Bttv,
    Ffz,
}

/// <summary>
/// One emote as a template sees it: the code a viewer types, where it comes from, its Twitch kind
/// (<c>subscriptions</c>, <c>follower</c>, <c>bitstier</c>; null for a third-party emote) and its picture.
/// <see cref="InChannel"/> is true for an emote of the channel's own set and false for a global one.
/// </summary>
internal sealed record EmoteItem(
    string Code,
    EmoteOrigin Origin,
    bool InChannel,
    string? Type,
    string? ImageUrl
)
{
    public const string SubscriberType = "subscriptions";
    public const string FollowerType = "follower";
    public const string BitsType = "bitstier";

    /// <summary>The network's name as the channel's viewers know it.</summary>
    public string Provider =>
        Origin switch
        {
            EmoteOrigin.SevenTv => "7TV",
            EmoteOrigin.Bttv => "BTTV",
            EmoteOrigin.Ffz => "FFZ",
            _ => "Twitch",
        };

    public bool IsChannelSetOf(EmoteOrigin origin) => Origin == origin && InChannel;
}
