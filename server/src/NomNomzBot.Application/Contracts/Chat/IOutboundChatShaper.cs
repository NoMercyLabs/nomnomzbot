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
/// Shapes one outbound line for one platform (S010): chunks it to the platform's visible-character
/// budget on word boundaries (never mid-word/mid-emote, never dropping text), and — when the line is
/// identical to the last one actually sent on the same <c>queueKey</c> — appends an
/// invisible trailing variation so Twitch's identical-consecutive-message rejection never eats a
/// legitimately repeated line. The <see cref="BotEmittedLine"/> marker a caller already stamped on the
/// message rides the first chunk only and is never counted against the platform's character budget —
/// it must survive chunking so the loop guard keeps working on every chunk that reaches ingest.
/// </summary>
public interface IOutboundChatShaper
{
    /// <summary>
    /// Returns the ordered chunks to send. <c>provider</c> selects the character budget
    /// (<see cref="ChatPlatformLineLimits"/>); <c>queueKey</c> scopes the duplicate-line memory
    /// (conventionally <c>"{broadcasterId}:{provider}"</c>).
    /// </summary>
    IReadOnlyList<string> Shape(string provider, string queueKey, string stampedMessage);
}

/// <summary>Per-platform visible-character budgets (S010) a single chat line is chunked to.</summary>
public static class ChatPlatformLineLimits
{
    public const int DefaultLimit = 500;

    private static readonly IReadOnlyDictionary<string, int> Limits = new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["twitch"] = 500,
        ["kick"] = 500,
        ["youtube"] = 200,
        ["twitter"] = 280,
    };

    /// <summary>The visible-character budget for <paramref name="provider"/>, or <see cref="DefaultLimit"/>
    /// for an unrecognized provider (never zero, never unbounded).</summary>
    public static int For(string provider) =>
        Limits.TryGetValue(provider, out int limit) ? limit : DefaultLimit;
}

/// <summary>
/// Paces sends through one <c>queueKey</c> (per-channel-per-platform) to the platform's rate
/// limit via a token bucket, and coalesces concurrent identical sends under the same
/// <c>coalesceKey</c> so a burst of chatters triggering the same command doesn't post the same line N
/// times — only the first actually reaches the platform; the rest join its result. A send that the
/// platform rejects is never silently dropped: the caller's delegate result flows back to every joined
/// waiter honestly (S008d).
/// </summary>
public interface IChatSendQueue
{
    Task<bool> EnqueueAsync(
        string queueKey,
        string coalesceKey,
        Func<CancellationToken, Task<bool>> send,
        CancellationToken cancellationToken = default
    );
}
