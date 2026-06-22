// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The thin decoration orchestrator (chat-decoration spec §0/§3.1). It owns no enrichment logic: it seeds a mutable
/// <see cref="ChatDecorationContext"/> from the event — copies of the fragments (so decoration never mutates the event's
/// own fragments that other handlers read) plus the channel's resolved rules — then runs the discovered
/// <see cref="IChatDecorationAdapter"/> chain in <c>Order</c>, each gated by its own <c>AppliesTo</c> and best-effort
/// (a throwing adapter is logged and skipped, the message still emits). No provider HTTP happens here — adapters read
/// only cache (the refresh worker warms it, §3.6).
/// </summary>
public sealed class ChatMessageDecorator : IChatMessageDecorator
{
    // The decoration features and their default state. Third-party emote rendering is ON by default — the near-universal
    // want, matching every emote extension — and a channel opts OUT with an explicit toggle. Link preview is absent here
    // (opt-in, gated on its own toggle + viewer standing in its adapter).
    private static readonly (string Key, bool DefaultOn)[] DecorationFeatures =
    [
        ("use_7tv", true),
        ("use_bttv", true),
        ("use_ffz", true),
    ];

    // The channel's resolved decoration rules are cached briefly so the chat hot path does not hit the feature store
    // per message; a toggle change takes effect within this window.
    private static readonly TimeSpan RulesCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<IChatDecorationAdapter> _adapters;
    private readonly IFeatureService _features;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatMessageDecorator> _logger;

    public ChatMessageDecorator(
        IEnumerable<IChatDecorationAdapter> adapters,
        IFeatureService features,
        ICacheService cache,
        ILogger<ChatMessageDecorator> logger
    )
    {
        _adapters = adapters.OrderBy(adapter => adapter.Order).ToList();
        _features = features;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DecoratedChatMessage> DecorateAsync(
        ChatMessageReceivedEvent message,
        CancellationToken ct = default
    )
    {
        ChatDecorationContext context = new()
        {
            TwitchBroadcasterId = message.TwitchBroadcasterId,
            EnabledFeatures = await ResolveEnabledFeaturesAsync(message.BroadcasterId, ct),
            Fragments = message.Fragments.Select(Clone).ToList(),
        };

        foreach (IChatDecorationAdapter adapter in _adapters)
        {
            if (!adapter.AppliesTo(context))
                continue;

            try
            {
                await adapter.DecorateAsync(context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Decoration adapter {Adapter} threw; skipping it for this message.",
                    adapter.GetType().Name
                );
            }
        }

        return new DecoratedChatMessage { Fragments = context.Fragments };
    }

    // The set of enabled decoration feature keys for the channel: each feature ON unless an explicit toggle disables it
    // (emote features default ON). Cached per channel for a short window so the hot path does not query the feature store
    // per message.
    private async Task<IReadOnlySet<string>> ResolveEnabledFeaturesAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        string cacheKey = $"chat:decoration:rules:{broadcasterId}";
        HashSet<string>? cached = await _cache.GetAsync<HashSet<string>>(cacheKey, ct);
        if (cached is not null)
            return cached;

        Result<List<FeatureStatusDto>> features = await _features.GetFeaturesAsync(
            broadcasterId.ToString(),
            ct
        );
        Dictionary<string, bool> toggles = features.IsSuccess
            ? features
                .Value.GroupBy(feature => feature.FeatureKey)
                .ToDictionary(group => group.Key, group => group.Last().IsEnabled)
            : [];

        HashSet<string> enabled = new(StringComparer.Ordinal);
        foreach ((string key, bool defaultOn) in DecorationFeatures)
            if (toggles.TryGetValue(key, out bool on) ? on : defaultOn)
                enabled.Add(key);

        await _cache.SetAsync(cacheKey, enabled, RulesCacheTtl, ct);
        return enabled;
    }

    // A field-for-field copy so in-place enrichment (e.g. setting Emote) never touches the event's own fragments,
    // which sibling handlers still read.
    private static ChatMessageFragment Clone(ChatMessageFragment fragment) =>
        new()
        {
            Type = fragment.Type,
            Text = fragment.Text,
            EmoteId = fragment.EmoteId,
            EmoteSetId = fragment.EmoteSetId,
            EmoteOwnerId = fragment.EmoteOwnerId,
            EmoteFormats = fragment.EmoteFormats,
            Emote = fragment.Emote,
            CheermotePrefix = fragment.CheermotePrefix,
            CheermoteBits = fragment.CheermoteBits,
            CheermoteTier = fragment.CheermoteTier,
            MentionUserId = fragment.MentionUserId,
            MentionUserLogin = fragment.MentionUserLogin,
            MentionUserName = fragment.MentionUserName,
        };
}
