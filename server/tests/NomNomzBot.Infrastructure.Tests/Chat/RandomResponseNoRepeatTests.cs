// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Infrastructure.Chat.EventHandlers;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A random-response command must never speak the same line twice running. A uniform draw repeats
/// back-to-back 1-in-N of the time — with a 20-line pool that is every twentieth use, and in chat an
/// immediately repeated "random" line reads as the bot being broken rather than as chance. The pool must
/// still behave randomly otherwise: excluding only the previous line, never cycling or exhausting.
/// </summary>
public sealed class RandomResponseNoRepeatTests
{
    /// <summary>Reaches the private picker directly — it is pure given its pool and key, so driving it
    /// through the whole chat pipeline would prove the same thing far less precisely.</summary>
    private static string Pick(ChatMessageHandler handler, string[] pool, string key) =>
        (string)
            typeof(ChatMessageHandler)
                .GetMethod("PickResponse", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(handler, [pool, key])!;

    private static ChatMessageHandler Uninitialised() =>
        (ChatMessageHandler)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(ChatMessageHandler)
            );

    /// <summary>The dictionary is a field initialiser, which an uninitialised instance skips — set it so the
    /// picker under test has its state, without constructing the handler's whole dependency graph.</summary>
    private static ChatMessageHandler Handler()
    {
        ChatMessageHandler handler = Uninitialised();
        FieldInfo field = typeof(ChatMessageHandler).GetField(
            "_lastResponseIndex",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        field.SetValue(handler, Activator.CreateInstance(field.FieldType));
        return handler;
    }

    [Fact]
    public void A_line_is_never_spoken_twice_in_a_row()
    {
        ChatMessageHandler handler = Handler();
        string[] pool = [.. Enumerable.Range(0, 5).Select(i => $"line-{i}")];

        string previous = Pick(handler, pool, "chan:roast");
        for (int i = 0; i < 2000; i++)
        {
            string next = Pick(handler, pool, "chan:roast");
            next.Should().NotBe(previous, "an immediately repeated line reads as a broken bot");
            previous = next;
        }
    }

    [Fact]
    public void Every_other_line_stays_reachable_so_the_pool_never_cycles_or_narrows()
    {
        ChatMessageHandler handler = Handler();
        string[] pool = [.. Enumerable.Range(0, 5).Select(i => $"line-{i}")];

        HashSet<string> seen = [];
        for (int i = 0; i < 2000; i++)
            seen.Add(Pick(handler, pool, "chan:roast"));

        // Avoiding the previous line must not turn the pool into a rotation or strand any line.
        seen.Should().HaveCount(pool.Length);
    }

    [Fact]
    public void Two_commands_do_not_share_one_anti_repeat_slot()
    {
        ChatMessageHandler handler = Handler();
        string[] pool = ["a", "b"];

        // With a two-line pool the next line is fully determined, so a shared slot would show up as one
        // command's draw dictating the other's. Each command alternates within ITS own history.
        string firstRoast = Pick(handler, pool, "chan:roast");
        string firstHug = Pick(handler, pool, "chan:hug");
        Pick(handler, pool, "chan:roast").Should().NotBe(firstRoast);
        Pick(handler, pool, "chan:hug").Should().NotBe(firstHug);
    }

    [Fact]
    public void A_single_line_pool_still_answers_rather_than_falling_silent()
    {
        ChatMessageHandler handler = Handler();

        Pick(handler, ["only"], "chan:solo").Should().Be("only");
        Pick(handler, ["only"], "chan:solo").Should().Be("only");
    }

    [Fact]
    public void An_empty_pool_yields_nothing_so_the_builtin_fallback_still_takes_over()
    {
        ChatMessageHandler handler = Handler();

        Pick(handler, [], "chan:none").Should().BeEmpty();
    }
}
