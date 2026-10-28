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
        context.Fragments.Any(fragment => fragment is { Type: "text", Text.Length: > 0 });

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
                exploded.Add(new() { Type = "text", Text = token });
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
