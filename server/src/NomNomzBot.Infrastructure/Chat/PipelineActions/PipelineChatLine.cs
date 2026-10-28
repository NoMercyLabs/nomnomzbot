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
