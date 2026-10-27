// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
                IsHostName(call.Args[0])
                    ? $"https://{call.Args[0]}/share?text={Uri.EscapeDataString(call.Args[1])}"
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
