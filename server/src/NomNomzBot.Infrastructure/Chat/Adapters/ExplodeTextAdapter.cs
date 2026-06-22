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
/// Pipeline step 10 (chat-decoration spec §0/§9·5): splits every <c>text</c> fragment into per-token fragments —
/// alternating whitespace runs and non-whitespace "words" — so the emote/url steps that follow can match a whole word
/// in place without re-tokenising. Whitespace is kept as its own fragment (never matched), so <c>ImplodeTextAdapter</c>
/// (step 80) reconstructs the exact original spacing around whatever is left unmatched. Non-text fragments pass through.
/// </summary>
public sealed class ExplodeTextAdapter : IChatDecorationAdapter
{
    public int Order => 10;

    public bool AppliesTo(ChatDecorationContext context) =>
        context.Fragments.Any(fragment => fragment.Type == "text" && fragment.Text.Length > 0);

    public Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        List<ChatMessageFragment> exploded = new(context.Fragments.Count);

        foreach (ChatMessageFragment fragment in context.Fragments)
        {
            if (fragment.Type != "text" || fragment.Text.Length == 0)
            {
                exploded.Add(fragment);
                continue;
            }

            foreach (string token in Tokenize(fragment.Text))
                exploded.Add(new ChatMessageFragment { Type = "text", Text = token });
        }

        context.Fragments.Clear();
        context.Fragments.AddRange(exploded);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Yields maximal runs of like characters — a whitespace run, then a non-whitespace run, alternating — so the
    /// tokens tile the input with zero loss (concatenating them reproduces the original string exactly).
    /// </summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        int index = 0;
        while (index < text.Length)
        {
            bool whitespace = char.IsWhiteSpace(text[index]);
            int start = index;
            while (index < text.Length && char.IsWhiteSpace(text[index]) == whitespace)
                index++;

            yield return text[start..index];
        }
    }
}
