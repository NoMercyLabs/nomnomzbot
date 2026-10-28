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

using FluentAssertions;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the bulk "remove all songs of one requester" action takes out exactly that requester's queued
/// songs, reports the true count, and leaves every other requester's songs in order.
/// </summary>
public sealed class RemoveAllFromRequesterTests
{
    private static readonly Guid ChannelId = MusicServiceQueueRefundTests.ChannelId;

    [Fact]
    public async Task Removes_only_the_named_requesters_queued_songs_and_returns_the_count()
    {
        (MusicService sut, _, ISongRequestQueueStore store) = MusicServiceQueueRefundTests.Build();
        Seed(store, "spotify:track:a1", "Bamo16");
        Seed(store, "spotify:track:b1", "Other");
        Seed(store, "spotify:track:a2", "bamo16");
        Seed(store, "spotify:track:a3", "Bamo16");

        int removed = await sut.RemoveAllFromRequesterAsync(ChannelId.ToString(), "Bamo16");

        removed.Should().Be(3);
        MusicQueue queue = await sut.GetQueueAsync(ChannelId.ToString());
        queue.Queue.Should().ContainSingle();
        queue.Queue[0].RequestedBy.Should().Be("Other");
    }

    [Fact]
    public async Task Returns_zero_and_changes_nothing_for_a_requester_with_no_queued_songs()
    {
        (MusicService sut, _, ISongRequestQueueStore store) = MusicServiceQueueRefundTests.Build();
        Seed(store, "spotify:track:b1", "Other");

        int removed = await sut.RemoveAllFromRequesterAsync(ChannelId.ToString(), "Nobody");

        removed.Should().Be(0);
        (await sut.GetQueueAsync(ChannelId.ToString())).Queue.Should().HaveCount(1);
    }

    private static void Seed(ISongRequestQueueStore store, string uri, string requestedBy) =>
        MusicServiceQueueRefundTests.SeedPaidEntry(
            store,
            uri,
            cost: 0,
            requesterUserId: null,
            requestedBy
        );
}
