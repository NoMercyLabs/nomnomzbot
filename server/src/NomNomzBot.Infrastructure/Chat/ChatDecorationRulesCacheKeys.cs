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
/// The cache-key scheme for a channel's resolved chat-decoration rules (chat-decoration spec §0/§7). Shared by the
/// reader (<c>ChatMessageDecorator</c>, which caches the resolved enabled-feature set for 60s off the chat hot path)
/// and the invalidator (<c>ChatDecorationRulesCacheInvalidator</c>, which evicts it the instant a toggle changes),
/// so both agree on the exact key and a toggle can never be invalidated against a key the reader never used.
/// </summary>
internal static class ChatDecorationRulesCacheKeys
{
    /// <summary><c>chat:decoration:rules:{broadcasterId}</c> — a channel's resolved decoration feature set.</summary>
    public static string Channel(Guid broadcasterId) => $"chat:decoration:rules:{broadcasterId}";
}
