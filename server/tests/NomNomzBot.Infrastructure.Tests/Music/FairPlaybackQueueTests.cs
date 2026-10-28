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
using NomNomzBot.Infrastructure.Music.Playback;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

public sealed class FairPlaybackQueueTests
{
    private static List<string> Ids(FairPlaybackQueue queue) =>
        [.. queue.Snapshot.Select(i => i.Id)];

    [Fact]
    public void RequeueAtHead_PutsItemAtHeadAndKeepsOrderBelow()
    {
        FairPlaybackQueue queue = new();
        queue.Enqueue(Item("a", owner: "alice"));
        queue.Enqueue(Item("b", owner: "bob"));
        queue.Enqueue(Item("c", owner: "carol"));
        queue.Enqueue(Item("a2", owner: "alice"));
        List<string> before = Ids(queue);

        queue.RequeueAtHead(Item("prev", owner: "alice"));

        queue.Head!.Id.Should().Be("prev");
        Ids(queue).Skip(1).Should().Equal(before);
    }

    [Fact]
    public void RequeueAtHead_OnEmptyQueue_MakesItemTheHead()
    {
        FairPlaybackQueue queue = new();

        queue.RequeueAtHead(Item("prev"));

        Ids(queue).Should().Equal("prev");
    }

    [Fact]
    public void Remove_RemovesOnlyThatRow_AndLeavesRowsAboveAndBelow()
    {
        FairPlaybackQueue queue = new();
        queue.Enqueue(Item("a", owner: "alice"));
        queue.Enqueue(Item("b", owner: "bob"));
        queue.Enqueue(Item("c", owner: "carol"));

        bool removed = queue.Remove("b");

        removed.Should().BeTrue();
        Ids(queue).Should().Equal("a", "c");
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalseAndChangesNothing()
    {
        FairPlaybackQueue queue = new();
        queue.Enqueue(Item("a"));

        queue.Remove("nope").Should().BeFalse();

        Ids(queue).Should().Equal("a");
    }

    [Fact]
    public void Remove_RowSixteenOfSixteen_RemovesOnlyRowSixteen()
    {
        FairPlaybackQueue queue = new();
        for (int i = 1; i <= 16; i++)
            queue.Enqueue(Item($"r{i}", owner: $"owner{i}"));
        List<string> before = Ids(queue);

        queue.Remove(before[15]).Should().BeTrue();

        Ids(queue).Should().Equal(before.Take(15));
    }
}
