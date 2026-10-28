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

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>A YouTube web search answer (<c>/youtubei/v1/search</c>) listing on-demand videos by id, in order.</summary>
internal static class YouTubeWebSearchFixture
{
    public static string Json(params string[] videoIds)
    {
        string items = string.Join(
            ",",
            videoIds.Select(id =>
                "{\"videoRenderer\":{\"videoId\":\""
                + id
                + "\",\"title\":{\"runs\":[{\"text\":\"hit "
                + id
                + "\"}]},\"ownerText\":{\"runs\":[{\"text\":\"Some Channel\"}]},"
                + "\"lengthText\":{\"simpleText\":\"3:30\"}}}"
            )
        );
        return "{\"contents\":{\"twoColumnSearchResultsRenderer\":{\"primaryContents\":"
            + "{\"sectionListRenderer\":{\"contents\":[{\"itemSectionRenderer\":{\"contents\":["
            + items
            + "]}}]}}}}}";
    }
}
