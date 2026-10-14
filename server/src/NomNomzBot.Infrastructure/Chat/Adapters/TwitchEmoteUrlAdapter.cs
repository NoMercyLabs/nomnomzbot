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

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 30 (chat-decoration spec §0/§4/§9·13): gives Twitch's own emotes the same unified
/// <see cref="ChatEmote"/> shape the third-party step produces, so the client renders every emote identically. Twitch
/// emote urls are a deterministic CDN template keyed on the EventSub-supplied id — there is no lookup and so no possible
/// miss. It only fills emote fragments the EventSub payload already flagged (<c>EmoteId</c> set) that the third-party
/// step did not already claim (<c>Emote is null</c>); animated emotes use the animated format when the payload offers it.
/// The emote's type, tier and owner come from the cached Twitch channel and global sets (cache only, never Helix); an
/// emote the cache does not know keeps just the owner id, with the owner's name when it is cached.
/// </summary>
public sealed class TwitchEmoteUrlAdapter : IChatDecorationAdapter
{
    private readonly ICacheService _cache;

    public TwitchEmoteUrlAdapter(ICacheService cache)
    {
        _cache = cache;
    }

    public int Order => 30;

    public bool AppliesTo(ChatDecorationContext context) =>
        context.Fragments.Any(IsUnresolvedTwitchEmote);

    public async Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        Dictionary<string, ChatEmote> known = await KnownEmotesAsync(
            context.TwitchBroadcasterId,
            ct
        );

        foreach (ChatMessageFragment fragment in context.Fragments)
            if (IsUnresolvedTwitchEmote(fragment))
                fragment.Emote = await BuildTwitchEmoteAsync(fragment, known, ct);
    }

    private static bool IsUnresolvedTwitchEmote(ChatMessageFragment fragment) =>
        fragment is { Type: "emote", Emote: null } && !string.IsNullOrEmpty(fragment.EmoteId);

    // The cached channel set wins over the global set for the same id.
    private async Task<Dictionary<string, ChatEmote>> KnownEmotesAsync(
        string twitchBroadcasterId,
        CancellationToken ct
    )
    {
        Dictionary<string, ChatEmote> known = new(StringComparer.Ordinal);
        IReadOnlyList<ChatEmote>? channel = string.IsNullOrEmpty(twitchBroadcasterId)
            ? null
            : await _cache.GetAsync<IReadOnlyList<ChatEmote>>(
                ChatEmoteCacheKeys.Channel(EmoteProvider.Twitch, twitchBroadcasterId),
                ct
            );
        IReadOnlyList<ChatEmote>? global = await _cache.GetAsync<IReadOnlyList<ChatEmote>>(
            ChatEmoteCacheKeys.Global(EmoteProvider.Twitch),
            ct
        );

        foreach (ChatEmote emote in (channel ?? []).Concat(global ?? []))
            known.TryAdd(emote.Id, emote);

        return known;
    }

    private async Task<ChatEmote> BuildTwitchEmoteAsync(
        ChatMessageFragment fragment,
        Dictionary<string, ChatEmote> known,
        CancellationToken ct
    )
    {
        bool animated = fragment.EmoteFormats.Contains("animated");
        string? ownerId = string.IsNullOrEmpty(fragment.EmoteOwnerId)
            ? null
            : fragment.EmoteOwnerId;
        known.TryGetValue(fragment.EmoteId!, out ChatEmote? cached);

        return new(
            EmoteProvider.Twitch,
            fragment.EmoteId!,
            fragment.Text,
            TwitchEmoteMapper.BuildUrls(fragment.EmoteId!, animated),
            animated,
            ZeroWidth: false,
            SetId: fragment.EmoteSetId,
            OwnerId: ownerId,
            Formats: fragment.EmoteFormats,
            Type: cached?.Type,
            Tier: cached?.Tier,
            Owner: cached?.Owner ?? await OwnerFromIdAsync(ownerId, ct),
            StaticUrls: TwitchEmoteMapper.BuildStaticUrls(fragment.EmoteId!)
        );
    }

    private async Task<ChatEmoteOwner?> OwnerFromIdAsync(string? ownerId, CancellationToken ct)
    {
        if (ownerId is null)
            return null;

        ChatEmoteOwner? cached = await _cache.GetAsync<ChatEmoteOwner>(
            ChatEmoteCacheKeys.TwitchOwner(ownerId),
            ct
        );
        return cached ?? new ChatEmoteOwner(ownerId, null, null);
    }
}
