// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The cache-key scheme for the chat-decoration Helix badge sets (chat-decoration spec §7). Shared by the reader
/// (<c>ChatBadgeResolver</c>) and the refresh worker. Keyed by the tenant broadcaster id, matching how the Helix
/// channel-badge call is addressed.
/// </summary>
internal static class ChatBadgeCacheKeys
{
    /// <summary>The global badge set (every channel).</summary>
    public const string Global = "chat:badges:global";

    /// <summary>A channel's own badge set (subscriber tiers, bits tiers, etc.).</summary>
    public static string Channel(Guid broadcasterId) => $"chat:badges:channel:{broadcasterId}";
}
