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

/// <summary>
/// The emotes chat uses most, counted over the newest <see cref="EmoteData.UsageWindow"/> chat lines: the whole
/// channel, this stream, and the viewer who typed the command. An emote nobody used prints nothing.
/// </summary>
internal sealed class EmoteUsageHelpers()
    : EmoteHelperGroup(
        Row("emote.channel.top", "KEKW", UsageSpan.Channel, (top, _) => top.Key),
        Row("emote.channel.top.stream", "catJAM", UsageSpan.Stream, (top, _) => top.Key),
        Row("emote.user.top", "KEKW", UsageSpan.Viewer, (top, _) => top.Key).ForViewer(),
        Row(
                "emote.user.top.count",
                "212",
                UsageSpan.Viewer,
                (top, call) => EmoteText.Count(top.Value, call.Culture)
            )
            .ForViewer()
    )
{
    private enum UsageSpan
    {
        Channel,
        Stream,
        Viewer,
    }

    private static EmoteRow Row(
        string key,
        string sample,
        UsageSpan span,
        Func<KeyValuePair<string, int>, EmoteCall, string> print
    ) =>
        EmoteRow.Custom(
            key,
            sample,
            EmoteSources.Catalogue | EmoteSources.Usage,
            async call => await TopAsync(call, span) is { } top ? print(top, call) : string.Empty
        );

    /// <summary>The most used emote and its count among the lines of the span; null when none was used.</summary>
    private static async Task<KeyValuePair<string, int>?> TopAsync(EmoteCall call, UsageSpan span)
    {
        IEnumerable<EmoteChatLine> lines = await call.Data.LinesAsync();
        if (span == UsageSpan.Stream)
        {
            string? stream = call.Scope.Channel?.CurrentStreamId;
            lines = stream is null ? [] : lines.Where(line => line.StreamId == stream);
        }
        else if (span == UsageSpan.Viewer)
        {
            string? who = await call.Data.ViewerPlatformIdAsync();
            lines = who is null ? [] : lines.Where(line => line.UserId == who);
        }

        IReadOnlyList<EmoteItem> known = await call.Data.AllAsync();
        return EmoteText.Top(EmoteText.Tally(lines.Select(line => line.Text), known));
    }
}
