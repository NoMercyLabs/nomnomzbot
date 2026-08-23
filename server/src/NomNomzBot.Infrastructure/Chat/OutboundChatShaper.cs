// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

    private readonly ConcurrentDictionary<string, string> _lastSentBody = new();

    public IReadOnlyList<string> Shape(string provider, string queueKey, string stampedMessage)
    {
        bool hasMarker = stampedMessage.StartsWith(BotEmittedLine.Marker, StringComparison.Ordinal);
        string body = hasMarker ? stampedMessage[BotEmittedLine.Marker.Length..] : stampedMessage;

        string variedBody = VaryIfRepeated(queueKey, body);

        int limit = ChatPlatformLineLimits.For(provider);
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
            int breakAt = text.LastIndexOf(' ', candidateEnd - 1, candidateEnd - start);
            bool hardSplit = breakAt <= start;
            if (hardSplit)
                breakAt = candidateEnd;

            chunks.Add(text[start..breakAt]);
            start =
                !hardSplit && breakAt < text.Length && text[breakAt] == ' ' ? breakAt + 1 : breakAt;
        }

        return chunks;
    }
}
