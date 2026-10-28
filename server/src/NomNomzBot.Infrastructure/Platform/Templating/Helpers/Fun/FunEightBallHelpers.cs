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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>
/// <c>fun.8ball</c>: one answer from the channel's own pick list named <c>8ball</c>. The answers are channel data
/// (the streamer edits them on the Pick Lists page), so no answer is written in code. A channel with no such list,
/// an empty list or a render with no channel prints the token as written.
/// </summary>
internal sealed class FunEightBallHelpers()
    : FunHelperGroup(FunRow.Channel("fun.8ball", "It is certain", Answer))
{
    public const string ListName = "8ball";

    private static async ValueTask<string?> Answer(FunCall call)
    {
        if (call.Args.Count != 0 || !call.Lists.HasChannel)
            return null;

        IReadOnlyList<string> answers = await call.Lists.ItemsAsync(ListName, call.Ct);
        return answers.Count == 0 ? null : FunCatalog.Pick(answers, call.Random);
    }
}
