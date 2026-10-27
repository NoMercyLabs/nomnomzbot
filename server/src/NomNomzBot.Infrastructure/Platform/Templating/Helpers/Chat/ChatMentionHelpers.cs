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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>Names written into chat: with an @ that pings, without one, or with a zero-width space that keeps the ping away.</summary>
internal sealed class ChatMentionHelpers()
    : ChatHelperGroup(
        ChatRow.OfText(
            "chat.mention",
            "@ViewerTwo",
            (name, call) => "@" + ChatText.MentionName(name, call)
        ),
        ChatRow.OfText("chat.mention.off", "ViewerTwo", (name, _) => ChatText.BareName(name)),
        ChatRow.OfText(
            "chat.mention.soft",
            "V​iewerTwo",
            (name, _) => Soft(ChatText.BareName(name))
        ),
        ChatRow.Seeded("chat.mention.user", "@ViewerOne", MentionUser),
        ChatRow.OfText("chat.me", "/me dances", (text, call) => Action(text, call.Provider))
    )
{
    private const string ZeroWidthSpace = "​";

    private static string? MentionUser(ChatCall call) =>
        call.Seed("user") is { } user
            ? "@" + ChatMentionName.For(user, call.Seed("user.name"))
            : null;

    /// <summary>The name with a zero-width space after its first character, so chat does not link it.</summary>
    private static string Soft(string name)
    {
        List<string> elements = ChatText.Elements(name);
        return elements.Count < 2
            ? name
            : elements[0] + ZeroWidthSpace + string.Concat(elements.Skip(1));
    }

    /// <summary>Twitch shows <c>/me</c> as an action line; other platforms get the plain text.</summary>
    private static string Action(string text, string provider) =>
        string.Equals(provider, "twitch", StringComparison.OrdinalIgnoreCase)
            ? "/me " + text
            : text;
}
