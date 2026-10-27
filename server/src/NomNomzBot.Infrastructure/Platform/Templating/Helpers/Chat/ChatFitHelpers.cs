// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Contracts.Chat;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>The line limit of the delivering platform, and text cut to fit it. Length counts text elements.</summary>
internal sealed class ChatFitHelpers()
    : ChatHelperGroup(
        ChatRow.Pure(
            "chat.limit",
            "500",
            0,
            0,
            call => ChatPlatformLineLimits.For(call.Provider).ToString(CultureInfo.InvariantCulture)
        ),
        ChatRow.Pure(
            "chat.fit",
            "short text",
            1,
            2,
            call => Fit(call.Args[0], EndOf(call.Args, " …"), call, atWord: false)
        ),
        ChatRow.OfText(
            "chat.fit.words",
            "short text",
            (text, call) => Fit(text, " …", call, atWord: true)
        )
    )
{
    /// <summary>The end text after one space; the default is an ellipsis.</summary>
    private static string EndOf(IReadOnlyList<string> args, string fallback) =>
        args.Count > 1 && args[1].Length > 0 ? " " + args[1] : fallback;

    private static string Fit(string text, string tail, ChatCall call, bool atWord)
    {
        int limit = ChatPlatformLineLimits.For(call.Provider);
        List<string> elements = ChatText.Elements(text);
        if (elements.Count <= limit)
            return text;

        int keep = limit - ChatText.Elements(tail).Count;
        if (keep <= 0)
            return ChatText.Head(ChatText.Elements(tail), limit);

        int cut = atWord ? WordEdge(elements, keep) : keep;
        return ChatText.Head(elements, cut) + tail;
    }

    /// <summary>The cut that ends on a word: keeps all <paramref name="keep"/> elements when the next one is a space.</summary>
    private static int WordEdge(List<string> elements, int keep)
    {
        if (string.IsNullOrWhiteSpace(elements[keep]))
            return keep;

        int edge = elements.FindLastIndex(keep - 1, e => string.IsNullOrWhiteSpace(e));
        return edge > 0 ? edge : keep;
    }
}
