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

using System.Globalization;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>
/// How many dice with how many sides: <c>20</c> is one 20-sided die, <c>2d6</c> is two six-sided dice, and no
/// argument at all is one six-sided die. Both numbers are capped so one template cannot flood the chat.
/// </summary>
internal readonly record struct FunDiceSpec(int Count, int Sides)
{
    public const int DefaultSides = 6;
    public const int MaxCount = 100;
    public const int MaxSides = 1000;

    public static bool TryParse(IReadOnlyList<string> args, out FunDiceSpec spec)
    {
        spec = new FunDiceSpec(1, DefaultSides);
        if (args.Count == 0)
            return true;
        if (args.Count != 1)
            return false;

        string text = args[0];
        int split = text.IndexOfAny(['d', 'D']);
        string countText = split <= 0 ? "1" : text[..split];
        string sidesText = split < 0 ? text : text[(split + 1)..];
        if (
            !TryWhole(countText, out int dice)
            || !TryWhole(sidesText, out int sides)
            || dice is < 1 or > MaxCount
            || sides is < 2 or > MaxSides
        )
            return false;

        spec = new FunDiceSpec(dice, sides);
        return true;
    }

    /// <summary>A whole number made of digits only.</summary>
    public static bool TryWhole(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public int[] Roll(Random random)
    {
        int[] rolls = new int[Count];
        for (int i = 0; i < rolls.Length; i++)
            rolls[i] = random.Next(1, Sides + 1);
        return rolls;
    }
}
