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

using System.Text;
using NomNomzBot.Application.Contracts.Tts;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// One spoken line, two voices: the bot's own words (the announcement) read in the channel's voice, and what the
/// chatter wrote (a resub message, a cheer message, a watch streak answer, a reward's input) read in the chatter's
/// own saved voice, as ONE clip. The words are found by marking them while the template renders, never by
/// searching the finished text, so a one-letter message cannot be mistaken for a letter in the announcement.
/// </summary>
internal static class TtsViewerWords
{
    private const char Open = '';
    private const char Close = '';

    /// <summary>The variables that hold what the viewer wrote.</summary>
    private static readonly string[] WordsKeys =
    [
        "message",
        "streak.message",
        "input",
        "user.message",
    ];

    /// <summary>A ready-made clause that quotes the viewer's words (" They also said: "..."").</summary>
    private const string QuoteClauseKey = "also_said";

    private const string OpeningQuotes = "\"“«";
    private const string ClosingQuotes = "\"”»";

    /// <summary>What the viewer wrote in these variables, trimmed, without duplicates; empty when they wrote nothing.</summary>
    public static IReadOnlyList<string> Words(IReadOnlyDictionary<string, string> variables) =>
        [
            .. WordsKeys
                .Select(key => variables.TryGetValue(key, out string? value) ? value.Trim() : "")
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal),
        ];

    /// <summary>
    /// A copy of the variables in which the viewer's words are wrapped in marks, also inside the quote clause that
    /// repeats them. Rendering a template with these gives a text whose marked stretches are the viewer's.
    /// </summary>
    public static Dictionary<string, string> Mark(IReadOnlyDictionary<string, string> variables)
    {
        IReadOnlyList<string> words = Words(variables);
        Dictionary<string, string> marked = new(variables, StringComparer.OrdinalIgnoreCase);
        foreach (string key in WordsKeys)
            if (marked.TryGetValue(key, out string? value) && value.Trim().Length > 0)
                marked[key] = $"{Open}{value.Trim()}{Close}";

        if (marked.TryGetValue(QuoteClauseKey, out string? clause))
            foreach (string word in words.OrderByDescending(w => w.Length))
                clause = clause.Replace(word, $"{Open}{word}{Close}", StringComparison.Ordinal);
        if (clause is not null)
            marked[QuoteClauseKey] = clause;

        return marked;
    }

    /// <summary>
    /// Cuts a marked text into its parts: the bot's words and the viewer's. Null when the text holds no mark.
    /// A quotation mark that only frames the viewer's words is dropped, so the voice does not say it.
    /// </summary>
    public static List<TtsSpeakSegment>? Split(string markedText, string viewerTwitchUserId)
    {
        if (!markedText.Contains(Open))
            return null;

        List<(string Text, bool Viewer)> parts = [];
        StringBuilder current = new();
        bool inViewer = false;
        foreach (char c in markedText)
        {
            if (c is Open or Close)
            {
                Flush(parts, current, inViewer);
                inViewer = c == Open;
                continue;
            }

            current.Append(c);
        }

        Flush(parts, current, inViewer);

        List<TtsSpeakSegment> segments = [];
        for (int index = 0; index < parts.Count; index++)
        {
            (string text, bool viewer) = parts[index];
            if (!viewer)
            {
                if (index + 1 < parts.Count && parts[index + 1].Viewer)
                    text = DropTrailingQuote(text);
                if (index > 0 && parts[index - 1].Viewer)
                    text = DropLeadingQuote(text);
            }

            text = text.Trim();
            if (text.Length == 0)
                continue;

            segments.Add(
                viewer
                    ? new(text, Speaker: TtsSpeaker.Viewer, SpeakerTwitchUserId: viewerTwitchUserId)
                    : new(text, Speaker: TtsSpeaker.Bot)
            );
        }

        return segments.Count == 0 ? null : segments;
    }

    /// <summary>
    /// For a line that was already rendered (a reply the pipeline sent to chat) and so carries no marks: the
    /// viewer's words are found in it, the last whole-word match of the longest words first. Null when none is found.
    /// </summary>
    public static List<TtsSpeakSegment>? SplitRendered(
        string text,
        IReadOnlyDictionary<string, string> variables,
        string viewerTwitchUserId
    )
    {
        foreach (string word in Words(variables).OrderByDescending(w => w.Length))
        {
            int at = LastWholeWord(text, word);
            if (at < 0)
                continue;

            string marked = $"{text[..at]}{Open}{word}{Close}{text[(at + word.Length)..]}";

            return Split(marked, viewerTwitchUserId);
        }

        return null;
    }

    private static int LastWholeWord(string text, string word)
    {
        int at = text.LastIndexOf(word, StringComparison.Ordinal);
        while (at >= 0)
        {
            bool startsWord = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            int end = at + word.Length;
            bool endsWord = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsWord && endsWord)
                return at;

            at = at == 0 ? -1 : text.LastIndexOf(word, at - 1, StringComparison.Ordinal);
        }

        return -1;
    }

    private static void Flush(
        List<(string Text, bool Viewer)> parts,
        StringBuilder current,
        bool viewer
    )
    {
        if (current.Length > 0)
            parts.Add((current.ToString(), viewer));
        current.Clear();
    }

    private static string DropTrailingQuote(string text)
    {
        string trimmed = text.TrimEnd();

        return trimmed.Length > 0 && OpeningQuotes.Contains(trimmed[^1]) ? trimmed[..^1] : text;
    }

    private static string DropLeadingQuote(string text)
    {
        string trimmed = text.TrimStart();

        return trimmed.Length > 0 && ClosingQuotes.Contains(trimmed[0]) ? trimmed[1..] : text;
    }
}
