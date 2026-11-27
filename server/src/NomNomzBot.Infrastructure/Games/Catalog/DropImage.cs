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

using System.Globalization;
using System.Text;
using NomNomzBot.Application.Games;

namespace NomNomzBot.Infrastructure.Games.Catalog;

/// <summary>The picture a drop falls as instead of the pack's own payload art; an avatar is drawn round.</summary>
internal sealed record DropImage(string Src, bool Round)
{
    // Pinned so a twemoji release can never rename a file under a running overlay.
    private const string TwemojiBase =
        "https://cdn.jsdelivr.net/gh/jdecked/twemoji@16.0.1/assets/svg/";

    private const int MaxEmojiLength = 32;
    private const int ZeroWidthJoiner = 0x200D;
    private const int EmojiPresentation = 0xFE0F;
    private const int Keycap = 0x20E3;

    /// <summary>
    /// The picture the first word after the stake names (drop-game spec section 5): <c>me</c> is the viewer's own
    /// avatar, an emote of the message is its platform image, and an emoji is its twemoji picture. Anything else,
    /// a link included, is no picture: the server only ever builds links from these fixed sources.
    /// </summary>
    public static DropImage? Pick(LiveGameInput input)
    {
        string? word = input.Args.FirstOrDefault(arg => !long.TryParse(arg, out _));
        if (word is null)
            return null;

        if (string.Equals(word, "me", StringComparison.OrdinalIgnoreCase))
            return IsHttps(input.Media?.AvatarUrl) ? new(input.Media!.AvatarUrl!, true) : null;

        LiveGameInputEmote? emote = input.Media?.Emotes.FirstOrDefault(e =>
            string.Equals(e.Code, word, StringComparison.Ordinal)
        );
        if (emote is not null)
            return IsHttps(emote.ImageUrl) ? new(emote.ImageUrl, false) : null;

        string? file = TwemojiFile(word);
        return file is null ? null : new(TwemojiBase + file + ".svg", false);
    }

    private static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// The twemoji file name of a word that is one emoji and nothing else: its code points in lowercase hex,
    /// joined by dashes, with the emoji-presentation selector left out unless the sequence uses a joiner (the
    /// twemoji naming rule). Null when the word holds anything that is not part of an emoji.
    /// </summary>
    private static string? TwemojiFile(string word)
    {
        if (word.Length > MaxEmojiLength)
            return null;

        List<int> points = [.. word.EnumerateRunes().Select(r => r.Value)];
        bool keycap = points.Contains(Keycap);
        bool hasSymbol = false;
        foreach (int point in points)
        {
            if (IsSymbol(point))
                hasSymbol = true;
            else if (!IsEmojiJoinPart(point, keycap))
                return null;
        }
        if (!hasSymbol && !keycap)
            return null;

        bool joined = points.Contains(ZeroWidthJoiner);
        IEnumerable<int> named = joined ? points : points.Where(p => p != EmojiPresentation);
        return string.Join('-', named.Select(p => p.ToString("x", CultureInfo.InvariantCulture)));
    }

    private static bool IsSymbol(int point) =>
        point >= 0xA9 && Rune.GetUnicodeCategory(new Rune(point)) == UnicodeCategory.OtherSymbol;

    // The parts that only join or shape an emoji: the joiner, the presentation selector, skin tones, the tag
    // letters of the England, Scotland and Wales flags, and the digit, # or * of a keycap.
    private static bool IsEmojiJoinPart(int point, bool keycap) =>
        point is ZeroWidthJoiner or EmojiPresentation or Keycap
        || point is >= 0x1F3FB and <= 0x1F3FF
        || point is >= 0xE0020 and <= 0xE007F
        || (keycap && point is (>= 0x30 and <= 0x39) or 0x23 or 0x2A);
}
