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
using NomNomzBot.Application.Common.Picking;

namespace NomNomzBot.Application.Tests.Common;

/// <summary>
/// The shared uniform-except-the-previous-pick RNG every "speak a random line" surface delegates to
/// (random-response commands, personality flavoring, pick-lists, quotes, template random helpers, TTS
/// roulette). Proves the no-immediate-repeat guarantee, per-key isolation, and the degenerate pool sizes.
/// </summary>
public sealed class NoImmediateRepeatPickerTests
{
    private static string Key(string suffix) =>
        $"{nameof(NoImmediateRepeatPickerTests)}:{Guid.NewGuid()}:{suffix}";

    [Fact]
    public void NextIndex_never_repeats_the_previous_index_for_the_same_key()
    {
        string key = Key("indices");
        int previous = NoImmediateRepeatPicker.NextIndex(6, key);
        for (int i = 0; i < 2000; i++)
        {
            int next = NoImmediateRepeatPicker.NextIndex(6, key);
            next.Should().NotBe(previous);
            previous = next;
        }
    }

    [Fact]
    public void Every_index_stays_reachable_across_many_draws()
    {
        string key = Key("coverage");
        HashSet<int> seen = [];
        for (int i = 0; i < 2000; i++)
            seen.Add(NoImmediateRepeatPicker.NextIndex(6, key));

        seen.Should().HaveCount(6, "excluding only the previous pick must not strand any index");
    }

    [Fact]
    public void Unrelated_keys_never_influence_each_others_memory()
    {
        string keyA = Key("a");
        string keyB = Key("b");

        int firstA = NoImmediateRepeatPicker.NextIndex(2, keyA);
        int firstB = NoImmediateRepeatPicker.NextIndex(2, keyB);

        // A 2-item pool makes the next draw fully determined by "not the previous one" — so a shared memory
        // slot would show up immediately as one key's draw dictating the other's.
        NoImmediateRepeatPicker.NextIndex(2, keyA).Should().NotBe(firstA);
        NoImmediateRepeatPicker.NextIndex(2, keyB).Should().NotBe(firstB);
    }

    [Fact]
    public void A_single_item_pool_always_returns_index_zero()
    {
        string key = Key("solo");
        NoImmediateRepeatPicker.NextIndex(1, key).Should().Be(0);
        NoImmediateRepeatPicker.NextIndex(1, key).Should().Be(0);
    }

    [Fact]
    public void A_zero_count_pool_throws()
    {
        Action act = () => NoImmediateRepeatPicker.NextIndex(0, Key("empty"));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Pick_returns_the_item_at_the_drawn_index()
    {
        string[] items = ["red", "green", "blue"];
        string key = Key("colors");

        string picked = NoImmediateRepeatPicker.Pick(items, key);

        items.Should().Contain(picked);
    }

    [Fact]
    public void Pick_on_an_empty_list_throws()
    {
        Action act = () => NoImmediateRepeatPicker.Pick(Array.Empty<string>(), Key("empty-list"));
        act.Should().Throw<ArgumentException>();
    }
}
