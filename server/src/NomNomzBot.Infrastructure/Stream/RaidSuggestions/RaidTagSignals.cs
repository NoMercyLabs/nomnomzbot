// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Raids;

namespace NomNomzBot.Infrastructure.Stream.RaidSuggestions;

/// <summary>
/// Matches a stream's tags and title against a channel's keyword rules. Words are compared as
/// lower-case word lists, so "Cozy  Crafts!" matches the phrase "cozy crafts" but not "uncozy crafts".
/// </summary>
internal static class RaidTagSignals
{
    private static readonly Regex WordSplit = new(@"[^a-zA-Z0-9+#]+", RegexOptions.Compiled);

    /// <summary>The first rule (in list order) that matches the stream, or null.</summary>
    public static RaidKeywordRule? FindMatch(
        TwitchStream stream,
        IReadOnlyList<RaidKeywordRule> rules
    )
    {
        string paddedTitle = " " + Normalize(stream.Title) + " ";
        List<string> tags = stream.Tags.Select(Normalize).ToList();

        foreach (RaidKeywordRule rule in rules)
        {
            foreach (string word in rule.Words.Select(Normalize).Where(w => w.Length > 0))
            {
                if (rule.MatchTags && tags.Contains(word))
                    return rule;
                if (rule.MatchTitle && paddedTitle.Contains(" " + word + " "))
                    return rule;
            }
        }

        return null;
    }

    private static string Normalize(string? text) =>
        string.Join(
            ' ',
            WordSplit.Split(text ?? "").Where(t => t.Length > 0).Select(t => t.ToLowerInvariant())
        );
}
