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

using System.Globalization;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>The message that triggered the template, read from the trigger seeds.</summary>
internal sealed class ChatMessageHelpers()
    : ChatHelperGroup(
        ChatRow
            .Seeded("chat.message", "hello big world", call => call.Seed("message"))
            .For(MessageContexts, eventScoped: true, seedKey: "message"),
        ChatRow
            .Seeded(
                "chat.message.length",
                "15",
                call =>
                    call.Seed("message") is { } message
                        ? ChatText.Elements(message).Count.ToString(CultureInfo.InvariantCulture)
                        : null
            )
            .For(MessageContexts, eventScoped: true, seedKey: "message"),
        ChatRow
            .Seeded(
                "chat.message.noCommand",
                "hello big world",
                call =>
                    call.Seed("message") is { } message ? WithoutCommandWord(message, call) : null
            )
            .For(MessageContexts, eventScoped: true, seedKey: "message"),
        ChatRow
            .Seeded("chat.message.bits", "0", call => call.Seed("user.bits"))
            .For(TemplateHelperContexts.WithTriggerViewer, eventScoped: true, seedKey: "bits")
    )
{
    private static readonly IReadOnlyList<TemplateHelperContext> MessageContexts =
        TemplateHelperContexts.WithTriggerViewerAndDiscord;

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
