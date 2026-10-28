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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>The emotes in the message that triggered the template: how many, and the first one.</summary>
internal sealed class EmoteMessageHelpers()
    : EmoteHelperGroup(
        EmoteRow
            .Custom(
                "emote.message.count",
                "3",
                EmoteSources.Catalogue,
                async call => EmoteText.Count((await EmotesInAsync(call)).Count, call.Culture)
            )
            .ForViewer(),
        EmoteRow
            .Custom(
                "emote.message.first",
                "KEKW",
                EmoteSources.Catalogue,
                async call => (await EmotesInAsync(call)).FirstOrDefault() ?? string.Empty
            )
            .ForViewer()
    )
{
    /// <summary>The words of the trigger message that are emote codes of the channel, in order, repeats kept.</summary>
    private static async Task<List<string>> EmotesInAsync(EmoteCall call)
    {
        HashSet<string> codes = [.. (await call.Data.AllAsync()).Select(e => e.Code)];
        return [.. EmoteText.Words(call.Data.Message).Where(codes.Contains)];
    }
}
