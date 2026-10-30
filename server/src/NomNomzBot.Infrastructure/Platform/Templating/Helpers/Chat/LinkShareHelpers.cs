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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>Click-to-post links: the viewer opens the link and the network's compose box holds the text. Nothing is posted.</summary>
internal sealed class LinkShareHelpers()
    : ChatHelperGroup(
        Share(
            "link.x",
            "https://x.com/intent/post?text=Live%20now",
            "https://x.com/intent/post?text="
        ),
        Share(
            "link.bluesky",
            "https://bsky.app/intent/compose?text=Live%20now",
            "https://bsky.app/intent/compose?text="
        ),
        Share(
            "link.threads",
            "https://www.threads.net/intent/post?text=Live%20now",
            "https://www.threads.net/intent/post?text="
        ),
        ChatRow.Pure(
            "link.mastodon",
            "https://mastodon.social/share?text=Live%20now",
            2,
            2,
            call =>
                IsHostName(ParsedArgument.Word(call.Args[0]))
                    ? $"https://{ParsedArgument.Word(call.Args[0])}/share?text={Uri.EscapeDataString(call.Args[1])}"
                    : null
        )
    )
{
    private static ChatRow Share(string key, string sample, string prefix) =>
        ChatRow.OfText(key, sample, (text, _) => prefix + Uri.EscapeDataString(text));

    /// <summary>A dotted host name only, so the instance cannot carry a path, query, port or credentials.</summary>
    private static bool IsHostName(string instance) =>
        instance.Contains('.', StringComparison.Ordinal)
        && Uri.CheckHostName(instance) == UriHostNameType.Dns;
}
