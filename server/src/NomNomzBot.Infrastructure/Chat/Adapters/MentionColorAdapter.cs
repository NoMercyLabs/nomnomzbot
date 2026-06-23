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
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 60 (chat-decoration spec §0/§3.1): colours each <c>@mention</c> with the mentioned user's last-seen
/// chat colour, recalled from <see cref="IChatColorMemory"/> (learned from that user's own messages). Best-effort and
/// cache-only — a user we have not seen leaves the mention's colour null (the client falls back to a default).
/// </summary>
public sealed class MentionColorAdapter : IChatDecorationAdapter
{
    private readonly IChatColorMemory _colors;

    public MentionColorAdapter(IChatColorMemory colors)
    {
        _colors = colors;
    }

    public int Order => 60;

    public bool AppliesTo(ChatDecorationContext context) =>
        context.Fragments.Any(fragment =>
            fragment.Type == "mention" && !string.IsNullOrEmpty(fragment.MentionUserId)
        );

    public async Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        foreach (ChatMessageFragment fragment in context.Fragments)
        {
            if (fragment.Type != "mention" || string.IsNullOrEmpty(fragment.MentionUserId))
                continue;

            fragment.MentionColorHex = await _colors.GetAsync(
                context.BroadcasterId,
                fragment.MentionUserId,
                ct
            );
        }
    }
}
