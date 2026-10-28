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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 80 (chat-decoration spec §0): the inverse of <see cref="ExplodeTextAdapter"/>. After the matching
/// steps have replaced word fragments with <c>emote</c>/<c>cheermote</c>/<c>mention</c> fragments, this collapses each
/// remaining run of adjacent <c>text</c> fragments back into one — restoring the original spacing — while leaving the
/// non-text fragments standalone. A message with no third-party matches comes out byte-identical to how it went in.
/// </summary>
public sealed class ImplodeTextAdapter : IChatDecorationAdapter
{
    public int Order => 80;

    public bool AppliesTo(ChatDecorationContext context) => context.Fragments.Count > 1;

    public Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        List<ChatMessageFragment> imploded = new(context.Fragments.Count);
        StringBuilder? run = null;

        foreach (ChatMessageFragment fragment in context.Fragments)
        {
            if (fragment.Type == "text")
            {
                run ??= new();
                run.Append(fragment.Text);
                continue;
            }

            FlushRun(imploded, ref run);
            imploded.Add(fragment);
        }

        FlushRun(imploded, ref run);

        context.Fragments.Clear();
        context.Fragments.AddRange(imploded);
        return Task.CompletedTask;
    }

    private static void FlushRun(List<ChatMessageFragment> output, ref StringBuilder? run)
    {
        if (run is null)
            return;

        output.Add(new() { Type = "text", Text = run.ToString() });
        run = null;
    }
}
