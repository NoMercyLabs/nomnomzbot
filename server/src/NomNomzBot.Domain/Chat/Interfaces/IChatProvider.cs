// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.Interfaces;

/// <summary>
/// Domain-safe outcome of <see cref="IChatProvider.UnbanUserAsync"/> — no dependency on Application's
/// typed <c>Result</c> (Domain carries no external-facing result type, mirroring
/// <see cref="NomNomzBot.Domain.Music.Interfaces.MusicProviderFailureReason"/>), so a platform's honest
/// "there was nothing to lift" is never collapsed into the same bucket as a real transport/API failure.
/// </summary>
public enum ChatUnbanOutcome
{
    /// <summary>The platform confirmed the ban/timeout was lifted.</summary>
    Success,

    /// <summary>No ban was found to lift — the user was never banned (by us, or at all), or the ban was
    /// already lifted. Not an error: the end state the caller wanted already holds.</summary>
    NotFound,

    /// <summary>The unban did not go through for a reason other than "nothing to lift" — no usable
    /// token/connection, an unregistered provider, or the platform's API rejected the call.</summary>
    Failed,
}

/// <summary>
/// Abstraction for sending chat messages and performing moderation actions.
/// <c>broadcasterId</c> is the tenant (channel) <see cref="Guid"/>; the implementation resolves it to the
/// Twitch channel string id before any Helix call (the invariant: Twitch never receives a Guid).
/// <c>userId</c> targets are Twitch user string ids (they arrive from Twitch events / template vars).
/// </summary>
public interface IChatProvider
{
    /// <summary>
    /// Sends a chat message as the bot. Returns <c>true</c> when Twitch accepted the message; <c>false</c> when it
    /// could NOT be sent — no Twitch connection for the channel, the bot identity is unavailable, or Helix rejected
    /// the call (e.g. a dead/expired token). Callers that report an outcome to the operator (the dashboard send
    /// path) MUST honour <c>false</c> instead of assuming success. It never throws for an expected send failure, so
    /// a swallowed failure can never masquerade as a successful send.
    /// </summary>
    Task<bool> SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a chat message as the STREAMER'S OWN account, even when a dedicated bot account is
    /// connected for this channel — for content only the broadcaster can post as themselves (e.g. a
    /// subscriber-only emote the bot account isn't subscribed to and so can't render). On a platform
    /// with no bot/broadcaster distinction (every identity already IS the streamer's own token) this is
    /// identical to <see cref="SendMessageAsync"/>. Same false-on-failure contract; never throws for an
    /// expected send failure.
    /// </summary>
    Task<bool> SendMessageAsBroadcasterAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a chat message threaded as a reply to <paramref name="replyToMessageId"/>. Returns <c>true</c> when
    /// the reply was accepted; <c>false</c> when it could NOT be sent (no connection, dead token, or the platform
    /// rejected the reply form — e.g. Twitch refusing a reply to a deleted/invalid parent message). Never throws
    /// for an expected send failure, so a swallowed failure can never masquerade as a successful send. Callers
    /// that must still reach the user despite a rejected reply form fall back to <see cref="SendMessageAsync"/>
    /// with an inline mention.
    /// </summary>
    Task<bool> SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    );

    Task TimeoutUserAsync(
        Guid broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lifts a ban/timeout. Returns <see cref="ChatUnbanOutcome.NotFound"/> — not an error — when there
    /// was nothing to lift (the platform's honest answer, not swallowed into a bare <c>Task</c>); callers
    /// that report an outcome to the operator MUST honour it instead of assuming success.
    /// </summary>
    Task<ChatUnbanOutcome> UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    );

    Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    );
}
