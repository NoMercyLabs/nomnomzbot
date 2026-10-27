// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
