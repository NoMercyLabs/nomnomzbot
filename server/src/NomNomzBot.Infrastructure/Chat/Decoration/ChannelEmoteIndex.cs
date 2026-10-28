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

using System.Diagnostics.CodeAnalysis;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Decoration;

/// <summary>
/// A word→emote lookup over a channel's merged third-party emote sets, encoding the decided matching rules
/// (chat-decoration spec §9·5/§9·6): a <b>case-insensitive</b> exact match on the emote code, and a deterministic
/// precedence on a code collision — <b>the set passed earlier wins</b>. The caller passes sets already in precedence
/// order (per provider channel-before-global; across providers 7TV→BTTV→FFZ), so the first to claim a code keeps it.
/// </summary>
public sealed class ChannelEmoteIndex
{
    private readonly IReadOnlyDictionary<string, ChatEmote> _byCode;

    private ChannelEmoteIndex(IReadOnlyDictionary<string, ChatEmote> byCode) => _byCode = byCode;

    /// <summary>The number of distinct codes in the index.</summary>
    public int Count => _byCode.Count;

    /// <summary>Looks up a single word against the emote codes (case-insensitive); false when the word is not an emote.</summary>
    public bool TryMatch(string word, [NotNullWhen(true)] out ChatEmote? emote) =>
        _byCode.TryGetValue(word, out emote);

    /// <summary>
    /// Builds the lookup from emote sets given in precedence order. The first set to define a code (compared
    /// case-insensitively) wins; later duplicates are skipped — that is how channel-over-global and 7TV→BTTV→FFZ resolve.
    /// </summary>
    public static ChannelEmoteIndex Build(
        IReadOnlyList<IReadOnlyList<ChatEmote>> setsInPrecedenceOrder
    )
    {
        Dictionary<string, ChatEmote> byCode = new(StringComparer.OrdinalIgnoreCase);

        foreach (IReadOnlyList<ChatEmote> set in setsInPrecedenceOrder)
        foreach (ChatEmote emote in set)
            byCode.TryAdd(emote.Code, emote);

        return new(byCode);
    }
}
