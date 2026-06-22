// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Decoration;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 20 (chat-decoration spec §0/§3.1/§9·5-6): matches third-party (7TV/BTTV/FFZ) emotes in the message.
/// It reads each <b>enabled</b> provider's cached global + channel sets — never a provider HTTP call on the hot path,
/// that is the refresh worker's job (§3.6) — assembles them in precedence order (per provider channel-before-global;
/// across providers 7TV→BTTV→FFZ) into a <see cref="ChannelEmoteIndex"/>, then replaces every per-word <c>text</c>
/// fragment whose text is an emote code with an <c>emote</c> fragment carrying the resolved <see cref="ChatEmote"/>.
/// Best-effort: a cache miss simply yields no match (the word stays plain text — spec §9·13), never a broken image.
/// </summary>
public sealed class ThirdPartyEmoteAdapter : IChatDecorationAdapter
{
    // The fixed cross-provider precedence (spec §9·6) paired with each provider's per-channel feature gate. The first
    // provider in this order to claim a code keeps it; adding a provider is one more entry (and its emote adapter class).
    private static readonly (EmoteProvider Provider, string FeatureKey)[] Providers =
    [
        (EmoteProvider.SevenTv, "use_7tv"),
        (EmoteProvider.Bttv, "use_bttv"),
        (EmoteProvider.Ffz, "use_ffz"),
    ];

    private readonly ICacheService _cache;

    public ThirdPartyEmoteAdapter(ICacheService cache)
    {
        _cache = cache;
    }

    public int Order => 20;

    public bool AppliesTo(ChatDecorationContext context) =>
        Providers.Any(provider => context.EnabledFeatures.Contains(provider.FeatureKey))
        && context.Fragments.Any(fragment => fragment.Type == "text" && fragment.Text.Length > 0);

    public async Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        List<IReadOnlyList<ChatEmote>> setsInPrecedenceOrder = [];

        foreach ((EmoteProvider provider, string featureKey) in Providers)
        {
            if (!context.EnabledFeatures.Contains(featureKey))
                continue;

            // Channel set first (more specific), then the global set — the per-provider half of the precedence (§9·6).
            await AddIfPresentAsync(
                setsInPrecedenceOrder,
                ChatEmoteCacheKeys.Channel(provider, context.TwitchBroadcasterId),
                ct
            );
            await AddIfPresentAsync(setsInPrecedenceOrder, ChatEmoteCacheKeys.Global(provider), ct);
        }

        if (setsInPrecedenceOrder.Count == 0)
            return;

        ChannelEmoteIndex index = ChannelEmoteIndex.Build(setsInPrecedenceOrder);
        if (index.Count == 0)
            return;

        for (int i = 0; i < context.Fragments.Count; i++)
        {
            ChatMessageFragment fragment = context.Fragments[i];
            if (fragment.Type != "text")
                continue;

            if (index.TryMatch(fragment.Text, out ChatEmote? emote))
                context.Fragments[i] = new ChatMessageFragment
                {
                    Type = "emote",
                    Text = fragment.Text,
                    Emote = emote,
                };
        }
    }

    private async Task AddIfPresentAsync(
        List<IReadOnlyList<ChatEmote>> sets,
        string cacheKey,
        CancellationToken ct
    )
    {
        IReadOnlyList<ChatEmote>? set = await _cache.GetAsync<IReadOnlyList<ChatEmote>>(
            cacheKey,
            ct
        );
        if (set is { Count: > 0 })
            sets.Add(set);
    }
}
