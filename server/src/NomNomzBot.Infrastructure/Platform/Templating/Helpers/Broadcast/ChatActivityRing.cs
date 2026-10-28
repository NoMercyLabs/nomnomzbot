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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>One chat message as the ring remembers it.</summary>
internal readonly record struct ChatActivityEntry(DateTimeOffset At, string UserId, string Name);

/// <summary>
/// The last hour of chat, per channel, in memory: who wrote and when. It answers the rolling-window rows
/// (messages per minute, active chatters, the top chatter of the hour) without a database read per render.
/// </summary>
internal sealed class ChatActivityRing(TimeProvider clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    private const int MaxPerChannel = 100_000;

    private readonly ConcurrentDictionary<Guid, Channel> _channels = new();

    /// <summary>Remembers a message; entries older than the window and entries over the cap are dropped.</summary>
    public void Record(Guid channelId, string userId, string name)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Channel channel = _channels.GetOrAdd(channelId, _ => new Channel());
        lock (channel.Gate)
        {
            channel.Entries.Enqueue(new ChatActivityEntry(now, userId, name));
            Trim(channel.Entries, now);
        }
    }

    /// <summary>The messages of the last <paramref name="span"/>, oldest first.</summary>
    public IReadOnlyList<ChatActivityEntry> Since(Guid channelId, TimeSpan span)
    {
        if (!_channels.TryGetValue(channelId, out Channel? channel))
            return [];

        DateTimeOffset now = clock.GetUtcNow();
        lock (channel.Gate)
        {
            Trim(channel.Entries, now);
            return [.. channel.Entries.Where(e => e.At > now - span)];
        }
    }

    private static void Trim(Queue<ChatActivityEntry> entries, DateTimeOffset now)
    {
        while (
            entries.Count > 0
            && (entries.Count > MaxPerChannel || entries.Peek().At <= now - Window)
        )
            entries.Dequeue();
    }

    private sealed class Channel
    {
        public object Gate { get; } = new();

        public Queue<ChatActivityEntry> Entries { get; } = new();
    }
}
