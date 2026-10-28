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

using System.Collections.Concurrent;
using NomNomzBot.Application.Contracts.Chat;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// <see cref="IOutboundChatShaper"/> (S010): singleton so the per-queue-key "last line actually sent"
/// memory survives across the scoped <c>ChatPlatformRouter</c> instance created per request — the same
/// reason <c>BotSelfEchoGuard</c>'s identity cache is a singleton. Word-wraps to the platform's visible
/// budget and appends <see cref="VariationMarker"/> (a DIFFERENT invisible codepoint than
/// <see cref="BotEmittedLine.Marker"/>, so the two never collide and the loop guard's marker check still
/// matches exactly) when the line repeats the previous one verbatim.
/// </summary>
public sealed class OutboundChatShaper : IOutboundChatShaper
{
    /// <summary>ZERO WIDTH SPACE (U+200B) — invisible, distinct from <see cref="BotEmittedLine.Marker"/>
    /// (U+2063 INVISIBLE SEPARATOR), so a repeated line posts as a byte-distinct-but-visually-identical
    /// message instead of colliding with Twitch's identical-consecutive-message rejection.</summary>
    public const string VariationMarker = "​";

    /// <summary>Separator between list entries (a queue, a leaderboard); a split lands here when it can.</summary>
    private const string EntrySeparator = " | ";

    private readonly ConcurrentDictionary<string, string> _lastSentBody = new();

    public IReadOnlyList<string> Shape(string provider, string queueKey, string stampedMessage)
    {
        bool hasMarker = stampedMessage.StartsWith(BotEmittedLine.Marker, StringComparison.Ordinal);
        string body = hasMarker ? stampedMessage[BotEmittedLine.Marker.Length..] : stampedMessage;

        string variedBody = VaryIfRepeated(queueKey, body);

        // The markers are real characters the platform counts (a 500-char chunk plus the loop-guard
        // marker made Twitch cut the last character of the line), so reserve room for both.
        int limit =
            ChatPlatformLineLimits.For(provider)
            - Math.Max(BotEmittedLine.Marker.Length, VariationMarker.Length);
        List<string> chunks = ChunkByWordBoundary(variedBody, limit);

        if (hasMarker)
        {
            if (chunks.Count == 0)
                chunks.Add(BotEmittedLine.Marker);
            else
                chunks[0] = BotEmittedLine.Marker + chunks[0];
        }

        return chunks;
    }

    private string VaryIfRepeated(string queueKey, string body)
    {
        string varied = body;
        _lastSentBody.AddOrUpdate(
            queueKey,
            _ => varied,
            (_, previous) =>
            {
                if (string.Equals(previous, body, StringComparison.Ordinal))
                    varied = body + VariationMarker;
                return varied;
            }
        );
        return varied;
    }

    /// <summary>
    /// Greedy word-boundary wrap: never splits inside a word (an emote name, a mention) and never drops a
    /// character — the delimiting space between two chunks is consumed by the split, so
    /// <c>string.Join(" ", chunks)</c> reproduces <paramref name="text"/> exactly. A single token longer
    /// than <paramref name="limit"/> (no space to break on) is hard-split so the loop always progresses.
    /// </summary>
    private static List<string> ChunkByWordBoundary(string text, int limit)
    {
        List<string> chunks = [];
        if (text.Length == 0)
        {
            chunks.Add(text);
            return chunks;
        }

        int start = 0;
        while (start < text.Length)
        {
            int remaining = text.Length - start;
            if (remaining <= limit)
            {
                chunks.Add(text[start..]);
                break;
            }

            int candidateEnd = start + limit;
            string window = text[start..(candidateEnd + 1)];

            // Prefer the last entry separator that fits, then the last space, then a hard split.
            int entryBreak = window.LastIndexOf(EntrySeparator, StringComparison.Ordinal);
            if (entryBreak > 0)
            {
                chunks.Add(text[start..(start + entryBreak)]);
                start += entryBreak + EntrySeparator.Length;
                continue;
            }

            int breakAt = window.LastIndexOf(' ');
            if (breakAt > 0)
            {
                chunks.Add(text[start..(start + breakAt)]);
                start += breakAt + 1;
                continue;
            }

            chunks.Add(text[start..candidateEnd]);
            start = candidateEnd;
        }

        return chunks;
    }
}
