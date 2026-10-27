// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>The small text forms the chat rows share: names without the @, and time spans such as <c>1h 30m</c>.</summary>
internal static class ChatText
{
    /// <summary>A name as typed, without the leading @ and without surrounding space.</summary>
    public static string BareName(string name) => name.Trim().TrimStart('@');

    /// <summary>
    /// The name to write after an @ in chat: the triggering user's login when the name is theirs and differs
    /// from the login (a non-Latin display name), else the name as typed.
    /// </summary>
    public static string MentionName(string typed, ChatCall call)
    {
        string name = BareName(typed);
        string? user = call.Seed("user");
        return user is not null && string.Equals(name, user, StringComparison.OrdinalIgnoreCase)
            ? ChatMentionName.For(user, call.Seed("user.name"))
            : name;
    }

    /// <summary>A span in whole seconds as the largest units that fit: <c>3s</c>, <c>10m</c>, <c>1h 30m</c>, <c>2d</c>.</summary>
    public static string Span(long seconds)
    {
        long left = Math.Max(0, seconds);
        List<string> parts = [];
        foreach ((string unit, long size) in Units)
        {
            long whole = left / size;
            left %= size;
            if (whole > 0)
                parts.Add(whole + unit);
        }
        return parts.Count == 0 ? "0s" : string.Join(' ', parts);
    }

    /// <summary>The first <paramref name="keep"/> text elements, with trailing space removed.</summary>
    public static string Head(IReadOnlyList<string> elements, int keep) =>
        string.Concat(elements.Take(Math.Max(0, keep))).TrimEnd();

    /// <summary>The text elements of the text.</summary>
    public static List<string> Elements(string text) => TextArguments.Elements(text);

    private static readonly (string Unit, long Size)[] Units =
    [
        ("d", 86400),
        ("h", 3600),
        ("m", 60),
        ("s", 1),
    ];
}
