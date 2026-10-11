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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 60 (chat-decoration spec §0/§3.1): colours each <c>@mention</c> with the mentioned user's own Twitch
/// chat colour. The colour comes from <see cref="IChatColorMemory"/> first (learned from that user's own messages, or
/// fetched earlier); users the memory has never seen are fetched in one batched Get User Chat Color call and
/// remembered, including "no colour set". Best-effort: a failed call leaves the colour null and remembers nothing.
/// </summary>
public sealed class MentionColorAdapter : IChatDecorationAdapter
{
    private readonly IChatColorMemory _colors;
    private readonly ITwitchHelixClient _helix;

    public MentionColorAdapter(IChatColorMemory colors, ITwitchHelixClient helix)
    {
        _colors = colors;
        _helix = helix;
    }

    public int Order => 60;

    public bool AppliesTo(ChatDecorationContext context) => context.Fragments.Any(IsMention);

    public async Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        List<string> userIds = context
            .Fragments.Where(IsMention)
            .Select(fragment => fragment.MentionUserId!)
            .Distinct()
            .ToList();

        Dictionary<string, string?> colors = new();
        List<string> misses = [];
        foreach (string userId in userIds)
        {
            RememberedChatColor? remembered = await _colors.RecallAsync(
                context.BroadcasterId,
                userId,
                ct
            );
            if (remembered is null)
                misses.Add(userId);
            else
                colors[userId] = remembered.Hex;
        }

        if (misses.Count > 0)
            await FetchAsync(context.BroadcasterId, misses, colors, ct);

        foreach (ChatMessageFragment fragment in context.Fragments.Where(IsMention))
        {
            colors.TryGetValue(fragment.MentionUserId!, out string? hex);
            fragment.MentionColorHex = string.IsNullOrEmpty(hex) ? null : hex;
        }
    }

    private async Task FetchAsync(
        Guid broadcasterId,
        List<string> userIds,
        Dictionary<string, string?> colors,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<TwitchUserChatColor>> fetched =
            await _helix.Chat.GetUserChatColorsAsync(userIds, ct);
        if (fetched.IsFailure)
            return;

        foreach (string userId in userIds)
        {
            string? hex = fetched.Value.FirstOrDefault(color => color.UserId == userId)?.Color;
            await _colors.RememberFetchedAsync(broadcasterId, userId, hex, ct);
            colors[userId] = hex;
        }
    }

    private static bool IsMention(ChatMessageFragment fragment) =>
        fragment.Type == "mention" && !string.IsNullOrEmpty(fragment.MentionUserId);
}
