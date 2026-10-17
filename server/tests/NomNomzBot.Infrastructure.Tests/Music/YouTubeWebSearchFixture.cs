// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
