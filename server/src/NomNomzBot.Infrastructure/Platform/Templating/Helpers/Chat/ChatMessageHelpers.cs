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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>The message that triggered the template, read from the trigger seeds.</summary>
internal sealed class ChatMessageHelpers()
    : ChatHelperGroup(
        ChatRow.Seeded("chat.message", "hello big world", call => call.Seed("message")),
        ChatRow.Seeded(
            "chat.message.length",
            "15",
            call =>
                call.Seed("message") is { } message
                    ? ChatText.Elements(message).Count.ToString(CultureInfo.InvariantCulture)
                    : null
        ),
        ChatRow.Seeded(
            "chat.message.noCommand",
            "hello big world",
            call => call.Seed("message") is { } message ? WithoutCommandWord(message, call) : null
        ),
        ChatRow.Seeded("chat.message.bits", "0", call => call.Seed("user.bits"))
    )
{
    /// <summary>
    /// The message without its command word: when the first word starts with the channel's command prefix, that
    /// word and the spaces after it are dropped; any other message stays whole.
    /// </summary>
    private static string WithoutCommandWord(string message, ChatCall call)
    {
        string prefix = call.Scope.Channel?.CommandPrefix ?? "!";
        string trimmed = message.TrimStart();
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
            return message;

        int firstSpace = trimmed.IndexOf(' ');
        return firstSpace < 0 ? string.Empty : trimmed[firstSpace..].TrimStart();
    }
}
