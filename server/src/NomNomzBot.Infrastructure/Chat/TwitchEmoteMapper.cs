// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The one place a Helix emote becomes a <see cref="ChatEmote"/>: the CDN url template, the tier rule (Helix
/// "1000"/"2000"/"3000" become 1/2/3, for subscription emotes only) and the "globals" type of a global emote. The
/// catalogue, the cache warmer and the live-feed adapter all use it, so one emote has one shape everywhere.
/// </summary>
internal static class TwitchEmoteMapper
{
    /// <summary>The type a global emote carries; Get Global Emotes has no type field of its own.</summary>
    public const string GlobalType = "globals";

    private const string SubscriptionType = "subscriptions";

    // Twitch emote CDN v2: /{id}/{format}/{theme}/{scale}. Dark theme + the three offered scales.
    private static readonly (string Key, string Scale)[] Scales =
    [
        ("1", "1.0"),
        ("2", "2.0"),
        ("3", "3.0"),
    ];

    public static Dictionary<string, string> BuildUrls(string id, bool animated) =>
        BuildUrls(id, animated ? "animated" : "static");

    /// <summary>The still image of an emote: the "static" format exists for every Twitch emote, animated or not.</summary>
    public static Dictionary<string, string> BuildStaticUrls(string id) => BuildUrls(id, "static");

    private static Dictionary<string, string> BuildUrls(string id, string format)
    {
        Dictionary<string, string> urls = new(Scales.Length);
        foreach ((string key, string scale) in Scales)
            urls[key] = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/{format}/dark/{scale}";

        return urls;
    }

    /// <summary>Helix tier "1000"/"2000"/"3000" as 1/2/3; null for any other type or tier value.</summary>
    public static int? ParseTier(string? type, string? helixTier) =>
        type == SubscriptionType
            ? helixTier switch
            {
                "1000" => 1,
                "2000" => 2,
                "3000" => 3,
                _ => null,
            }
            : null;

    public static ChatEmote FromChannel(
        TwitchChannelEmote emote,
        string ownerId,
        ChatEmoteOwner? owner
    ) =>
        Build(
            emote.Id,
            emote.Name,
            emote.Format,
            emote.EmoteSetId,
            ownerId,
            emote.EmoteType,
            ParseTier(emote.EmoteType, emote.Tier),
            owner
        );

    public static ChatEmote FromGlobal(TwitchGlobalEmote emote) =>
        Build(emote.Id, emote.Name, emote.Format, null, null, GlobalType, null, null);

    public static ChatEmote FromUser(TwitchUserEmote emote) =>
        Build(
            emote.Id,
            emote.Name,
            emote.Format,
            emote.EmoteSetId,
            string.IsNullOrEmpty(emote.OwnerId) ? null : emote.OwnerId,
            emote.EmoteType,
            null,
            null
        );

    private static ChatEmote Build(
        string id,
        string code,
        IReadOnlyList<string> formats,
        string? setId,
        string? ownerId,
        string type,
        int? tier,
        ChatEmoteOwner? owner
    )
    {
        bool animated = formats.Contains("animated");

        return new(
            EmoteProvider.Twitch,
            id,
            code,
            BuildUrls(id, animated),
            animated,
            ZeroWidth: false,
            SetId: setId,
            OwnerId: ownerId,
            Formats: formats,
            Type: string.IsNullOrEmpty(type) ? null : type,
            Tier: tier,
            Owner: owner,
            StaticUrls: BuildStaticUrls(id)
        );
    }
}
