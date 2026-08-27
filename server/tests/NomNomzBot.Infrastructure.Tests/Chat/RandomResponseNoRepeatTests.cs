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

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A random-response command must never speak the same line twice running — <see cref="ChatMessageHandler"/>'s
/// <c>PickResponse</c> is a thin wrapper over the shared <see cref="NoImmediateRepeatPicker"/>, proven here
/// directly. A uniform draw repeats back-to-back 1-in-N of the time — with a 20-line pool that is every
/// twentieth use, and in chat an immediately repeated "random" line reads as the bot being broken rather than
/// as chance. The pool must still behave randomly otherwise: excluding only the previous line, never cycling
/// or exhausting.
/// </summary>
public sealed class RandomResponseNoRepeatTests
{
    // Every test uses a unique key prefix so the picker's process-wide static memory never leaks between tests.
    private static string Key(string suffix) =>
        $"{nameof(RandomResponseNoRepeatTests)}:{Guid.NewGuid()}:{suffix}";

    [Fact]
    public void A_line_is_never_spoken_twice_in_a_row()
    {
        string[] pool = [.. Enumerable.Range(0, 5).Select(i => $"line-{i}")];
        string key = Key("roast");

        string previous = NoImmediateRepeatPicker.Pick(pool, key);
        for (int i = 0; i < 2000; i++)
        {
            string next = NoImmediateRepeatPicker.Pick(pool, key);
            next.Should().NotBe(previous, "an immediately repeated line reads as a broken bot");
            previous = next;
        }
    }

    [Fact]
    public void Every_other_line_stays_reachable_so_the_pool_never_cycles_or_narrows()
    {
        string[] pool = [.. Enumerable.Range(0, 5).Select(i => $"line-{i}")];
        string key = Key("roast");

        HashSet<string> seen = [];
        for (int i = 0; i < 2000; i++)
            seen.Add(NoImmediateRepeatPicker.Pick(pool, key));

        // Avoiding the previous line must not turn the pool into a rotation or strand any line.
        seen.Should().HaveCount(pool.Length);
    }

    [Fact]
    public void Two_commands_do_not_share_one_anti_repeat_slot()
    {
        string[] pool = ["a", "b"];
        string roastKey = Key("roast");
        string hugKey = Key("hug");

        // With a two-line pool the next line is fully determined, so a shared slot would show up as one
        // command's draw dictating the other's. Each command alternates within ITS own history.
        string firstRoast = NoImmediateRepeatPicker.Pick(pool, roastKey);
        string firstHug = NoImmediateRepeatPicker.Pick(pool, hugKey);
        NoImmediateRepeatPicker.Pick(pool, roastKey).Should().NotBe(firstRoast);
        NoImmediateRepeatPicker.Pick(pool, hugKey).Should().NotBe(firstHug);
    }

    [Fact]
    public void A_single_line_pool_still_answers_rather_than_falling_silent()
    {
        string key = Key("solo");

        NoImmediateRepeatPicker.Pick(["only"], key).Should().Be("only");
        NoImmediateRepeatPicker.Pick(["only"], key).Should().Be("only");
    }

    [Fact]
    public void An_empty_pool_throws_so_the_caller_falls_back_explicitly()
    {
        // ChatMessageHandler.PickResponse checks Length == 0 itself and returns string.Empty before ever
        // calling the picker — the picker's own contract is to reject an empty pool outright.
        Action act = () => NoImmediateRepeatPicker.Pick(Array.Empty<string>(), Key("none"));

        act.Should().Throw<ArgumentException>();
    }
}
