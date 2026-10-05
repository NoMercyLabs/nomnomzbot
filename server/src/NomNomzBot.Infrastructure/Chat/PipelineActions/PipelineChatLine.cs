// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Chat.PipelineActions;

/// <summary>
/// The length rule for a chat line a pipeline sends (<c>send_message</c>, <c>send_reply</c>). The old bot
/// cut such a line at 450 characters: keep the first 447 and add "...". A line of 450 or fewer is sent as is.
/// </summary>
internal static class PipelineChatLine
{
    private const int MaxLength = 450;
    private const string Ellipsis = "...";

    public static string Cut(string line) =>
        line.Length <= MaxLength ? line : line[..(MaxLength - Ellipsis.Length)] + Ellipsis;
}
