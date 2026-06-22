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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
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
    // Third-party emote rendering is on by default for every channel — the near-universal want, matching every emote
    // extension. The per-channel opt-out (a cached IFeatureService lookup seeded here) is the next slice; link preview
    // is deliberately absent (opt-in, gated on its own toggle + viewer standing).
    private static readonly string[] DefaultEnabledFeatures = ["use_7tv", "use_bttv", "use_ffz"];

    private readonly IReadOnlyList<IChatDecorationAdapter> _adapters;
    private readonly ILogger<ChatMessageDecorator> _logger;

    public ChatMessageDecorator(
        IEnumerable<IChatDecorationAdapter> adapters,
        ILogger<ChatMessageDecorator> logger
    )
    {
        _adapters = adapters.OrderBy(adapter => adapter.Order).ToList();
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
            EnabledFeatures = DefaultEnabledFeatures.ToHashSet(StringComparer.Ordinal),
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
