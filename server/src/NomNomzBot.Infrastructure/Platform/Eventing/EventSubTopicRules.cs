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

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// What a refused EventSub topic needs, for the types Twitch refuses with the unnamed "missing proper
/// authorization". Naming the scope turns an unfixable-looking refusal into one the streamer can act on.
/// </summary>
internal static class EventSubTopicRules
{
    /// <summary>Twitch's version string for a topic that is beta: refused on every channel until Twitch opens it.</summary>
    public const string BetaVersion = "beta";

    /// <summary>
    /// The scope the token owner must hold for this topic, or null when no single scope is known for it.
    /// </summary>
    public static string? RequiredScope(string eventType) =>
        eventType switch
        {
            "channel.subscribe"
            or "channel.subscription.end"
            or "channel.subscription.gift"
            or "channel.subscription.message" => TwitchScopes.ChannelReadSubscriptions,
            "channel.vip.add" or "channel.vip.remove" => TwitchScopes.ChannelReadVips,
            _ => null,
        };
}
