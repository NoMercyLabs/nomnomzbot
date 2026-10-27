// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Humanizer;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>Speech-ready text: fancy letters folded, links and @ dropped, emotes dropped or kept, numbers spoken. No helper picks a voice.</summary>
internal sealed partial class TtsTextHelpers()
    : ChatHelperGroup(
        new ChatRow(
            "tts.plain",
            "hi ViewerTwo hi",
            ChatSources.Emotes,
            1,
            1,
            async call =>
                Plain(call.Args[0], call.Data is { } data ? await data.EmoteCodesAsync() : [])
        )
        {
            Freshness = TemplateHelperFreshness.Cached,
            Scope = TemplateHelperScope.Channel,
        },
        ChatRow.OfText("tts.plain.keepEmotes", "hi KEKW", (text, _) => Plain(text, [])),
        ChatRow.OfText(
            "tts.numbers",
            "I have three cats",
            (text, call) => Numbers(text, call.Scope.Language)
        ),
        ChatRow.ReadText(
            "tts.lexicon",
            "good game",
            ChatSources.Lexicon,
            TemplateHelperFreshness.Cached,
            async (data, text, call) => await data.ApplyLexiconAsync(text, call.Ct)
        )
    )
{
    private static string Plain(string text, HashSet<string> emoteCodes)
    {
        string folded = Link().Replace(text.Normalize(NormalizationForm.FormKC), " ");
        IEnumerable<string> words = folded
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(ChatText.BareName)
            .Where(word => word.Length > 0 && !emoteCodes.Contains(word));
        return string.Join(' ', words);
    }

    /// <summary>Whole numbers become words in the channel language; a number with decimals is left as written.</summary>
    private static string Numbers(string text, string? language)
    {
        CultureInfo culture = NumberCulture.For(language);
        return WholeNumber()
            .Replace(
                text,
                match =>
                    long.TryParse(
                        match.Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out long value
                    )
                        ? value.ToWords(culture)
                        : match.Value
            );
    }

    [GeneratedRegex(
        @"(?:https?://|www\.)\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<![\d.,])\d{1,18}(?![\d]|[.,]\d)", RegexOptions.CultureInvariant)]
    private static partial Regex WholeNumber();
}
