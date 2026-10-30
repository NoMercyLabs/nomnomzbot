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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>
/// The chat games: rock paper scissors (<c>fun.rps</c>, <c>fun.rps.vs</c>), a playing card (<c>fun.card</c>), the
/// slot machine (<c>fun.slots</c>, <c>fun.slots.win</c>) and Russian roulette (<c>fun.roulette</c>).
/// </summary>
internal sealed class FunGameHelpers()
    : FunHelperGroup(
        FunRow.Pure("fun.rps", "🪨 rock", Hand),
        FunRow.Pure("fun.rps.vs", "paper — you lose", Versus),
        FunRow.Pure("fun.card", "Q♥", Card),
        FunRow.Pure("fun.slots", "🍒 🍋 🍒", Slots),
        FunRow.Pure("fun.slots.win", "false", SlotsWin),
        FunRow.Pure("fun.roulette", "click", Roulette)
    )
{
    private const string SlotsKey = "fun.slots.win";
    private const int SlotReels = 3;
    private const string Ranks = "A23456789TJQK";
    private const string Suits = "♠♥♦♣";
    private const int ChambersInGun = 6;

    private static string? Hand(FunCall call) =>
        call.Args.Count == 0 ? FunCatalog.Pick(FunCatalog.Hands, call.Random).Label : null;

    /// <summary><c>rps.vs:choice</c>: the bot draws a hand and says who won. The choice is rock, paper or scissors.</summary>
    private static string? Versus(FunCall call)
    {
        if (call.Args.Count != 1 || HandIndex(call.Args[0]) is not int mine)
            return null;

        int theirIndex = call.Random.Next(FunCatalog.Hands.Count);
        FunItem theirs = FunCatalog.Hands[theirIndex];
        string verdict =
            theirIndex == mine ? "a draw"
            : (mine + 2) % FunCatalog.Hands.Count == theirIndex ? "you win"
            : "you lose";
        return $"{theirs.Name} — {verdict}";
    }

    private static int? HandIndex(string argument)
    {
        string choice = ParsedArgument.Word(argument);
        for (int i = 0; i < FunCatalog.Hands.Count; i++)
            if (string.Equals(choice, FunCatalog.Hands[i].Name, StringComparison.OrdinalIgnoreCase))
                return i;
        return null;
    }

    private static string? Card(FunCall call)
    {
        if (call.Args.Count != 0)
            return null;

        int card = call.Random.Next(Ranks.Length * Suits.Length);
        char rank = Ranks[card % Ranks.Length];
        return (rank == 'T' ? "10" : rank.ToString()) + Suits[card / Ranks.Length];
    }

    /// <summary>Three reels. The render remembers whether all three matched, for <c>fun.slots.win</c>.</summary>
    private static string? Slots(FunCall call)
    {
        if (call.Args.Count != 0)
            return null;

        List<string> reels =
        [
            .. Enumerable
                .Range(0, SlotReels)
                .Select(_ => FunCatalog.Pick(FunCatalog.SlotSymbols, call.Random)),
        ];
        call.Scope.Items[SlotsKey] = reels.Distinct().Count() == 1;
        return string.Join(' ', reels);
    }

    /// <summary>True when the last <c>fun.slots</c> of this render matched on all reels; false when there was none.</summary>
    private static string? SlotsWin(FunCall call) =>
        call.Args.Count != 0 ? null
        : call.Scope.Items.TryGetValue(SlotsKey, out object? won) && won is true ? "true"
        : "false";

    private static string? Roulette(FunCall call) =>
        call.Args.Count != 0 ? null
        : call.Random.Next(ChambersInGun) == 0 ? "bang"
        : "click";
}
