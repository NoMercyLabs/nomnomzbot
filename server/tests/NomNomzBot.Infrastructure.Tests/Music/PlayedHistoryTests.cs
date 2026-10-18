// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.Playback;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

public sealed class PlayedHistoryTests
{
    [Fact]
    public void Push_KeepsOnlyTheLast20_DroppingTheOldest()
    {
        PlayedHistory history = new();
        for (int i = 1; i <= 25; i++)
            history.Push(Item($"p{i}"));

        history.Count.Should().Be(20);
        List<string> popped = [];
        while (history.Pop() is { } item)
            popped.Add(item.Id);
        popped.Should().Equal(Enumerable.Range(6, 20).Reverse().Select(n => $"p{n}"));
    }

    [Fact]
    public void Pop_ReturnsNewestFirst_AndNullWhenEmpty()
    {
        PlayedHistory history = new();
        history.Push(Item("one"));
        history.Push(Item("two"));

        history.Last!.Id.Should().Be("two");
        PlaybackItem? first = history.Pop();
        PlaybackItem? second = history.Pop();
        first!.Id.Should().Be("two");
        second!.Id.Should().Be("one");
        history.Pop().Should().BeNull();
        history.Last.Should().BeNull();
    }
}
