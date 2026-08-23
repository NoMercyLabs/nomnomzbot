// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Chat;

/// <summary>
/// The self-trigger guard (S009): decides, for one inbound chat line on one ingest, whether it is a line
/// the BOT ITSELF put in chat rather than a real chatter — so the three ingests (Twitch EventSub, Kick
/// webhook, YouTube poller) never hand a bot-authored line to <c>ChatMessageHandler</c> as a fresh command
/// trigger. Two independent signals feed the decision, because self-host (D5) rules out "ignore the
/// streamer's own account": until a dedicated bot account is connected, the bot TYPES AS the streamer's own
/// account, and that account's real human lines must still be honoured as commands.
///
/// 1. Sender-identity match — the tenant's resolved bot identity (a connected per-channel custom bot, else
///    the shared platform bot, else none) on the SAME provider as the inbound line. A dedicated bot's line
///    is dropped outright, on every provider, independent of content.
/// 2. The emitted-line marker (<see cref="BotEmittedLine"/>) — content carried on every line the bot itself
///    sent, present regardless of which account signed the send. This is the ONLY signal available for the
///    self-host case: same account, but a bot-emitted line carries the marker while a human-typed line does
///    not.
/// </summary>
public interface IBotSelfEchoGuard
{
    /// <summary>
    /// True when the inbound line at (<paramref name="tenantId"/>, <paramref name="provider"/>) should be
    /// suppressed from triggering commands — it is the bot's own voice, not a chatter's.
    /// </summary>
    Task<bool> ShouldSuppressAsync(
        Guid tenantId,
        string provider,
        string senderId,
        string message,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Cache-invalidation seam for <see cref="IBotSelfEchoGuard"/>'s per-tenant bot-identity resolution — split
/// out so the scoped event handlers that react to <c>BotAccountAuthorizedEvent</c> /
/// <c>BotAccountDisconnectedEvent</c> can evict the singleton guard's cache without depending on its full
/// (async, DB-touching) surface.
/// </summary>
public interface IBotIdentityCacheInvalidation
{
    /// <summary>Evicts one tenant's cached bot identity (a per-channel custom bot connected/disconnected).</summary>
    void InvalidateTenant(Guid tenantId);

    /// <summary>Evicts every tenant's cached bot identity (the shared platform bot connected/disconnected).</summary>
    void InvalidateAll();
}

/// <summary>
/// The content marker every bot-emitted chat line carries (D5's "user-defined line prefix" mechanism,
/// generalized to a fixed invisible marker): an <c>INVISIBLE SEPARATOR</c> (U+2063) that never renders and
/// never collides with anything a human would type, so a bot-emitted line remains recognizable as the
/// bot's own voice even when it rides the SAME account a human also chats from (self-host, no dedicated bot
/// connected). The send path stamps every outgoing line with this marker; the guard checks for its
/// presence to distinguish "the bot said this" from "the streamer typed this".
/// </summary>
public static class BotEmittedLine
{
    public const string Marker = "⁣";

    /// <summary>Stamps outgoing bot text with the marker so any ingest that echoes it back recognizes it.</summary>
    public static string Stamp(string message) => Marker + message;

    /// <summary>True when the inbound text carries the bot-emitted marker anywhere in its content.</summary>
    public static bool IsMarked(string message) =>
        message.Contains(Marker, StringComparison.Ordinal);
}
