// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 30 (chat-decoration spec §0/§4/§9·13): gives Twitch's own emotes the same unified
/// <see cref="ChatEmote"/> shape the third-party step produces, so the client renders every emote identically. Twitch
/// emote urls are a deterministic CDN template keyed on the EventSub-supplied id — there is no lookup and so no possible
/// miss. It only fills emote fragments the EventSub payload already flagged (<c>EmoteId</c> set) that the third-party
/// step did not already claim (<c>Emote is null</c>); animated emotes use the animated format when the payload offers it.
/// </summary>
public sealed class TwitchEmoteUrlAdapter : IChatDecorationAdapter
{
    // Twitch emote CDN v2: /{id}/{format}/{theme}/{scale}. Dark theme + the three offered scales.
    private static readonly (string Key, string Scale)[] Scales =
    [
        ("1", "1.0"),
        ("2", "2.0"),
        ("3", "3.0"),
    ];

    public int Order => 30;

    public bool AppliesTo(ChatDecorationContext context) =>
        context.Fragments.Any(IsUnresolvedTwitchEmote);

    public Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        foreach (ChatMessageFragment fragment in context.Fragments)
            if (IsUnresolvedTwitchEmote(fragment))
                fragment.Emote = BuildTwitchEmote(fragment);

        return Task.CompletedTask;
    }

    private static bool IsUnresolvedTwitchEmote(ChatMessageFragment fragment) =>
        fragment.Type == "emote"
        && fragment.Emote is null
        && !string.IsNullOrEmpty(fragment.EmoteId);

    private static ChatEmote BuildTwitchEmote(ChatMessageFragment fragment)
    {
        bool animated = fragment.EmoteFormats.Contains("animated");
        string format = animated ? "animated" : "static";

        Dictionary<string, string> urls = new(Scales.Length);
        foreach ((string key, string scale) in Scales)
            urls[key] =
                $"https://static-cdn.jtvnw.net/emoticons/v2/{fragment.EmoteId}/{format}/dark/{scale}";

        return new ChatEmote(
            EmoteProvider.Twitch,
            fragment.EmoteId!,
            fragment.Text,
            urls,
            animated,
            ZeroWidth: false,
            SetId: fragment.EmoteSetId,
            OwnerId: fragment.EmoteOwnerId,
            Formats: fragment.EmoteFormats
        );
    }
}
