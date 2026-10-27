// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>One thing a fun helper can print: an emoji (or none) and its name.</summary>
internal sealed record FunItem(string? Emoji, string Name)
{
    /// <summary>The emoji, a space and the name; the name alone for an item with no emoji.</summary>
    public string Label => Emoji is null ? Name : Emoji + " " + Name;
}

/// <summary>The fixed, safe sets the pure random rows draw from.</summary>
internal static class FunCatalog
{
    public static readonly IReadOnlyList<string> Emoji =
    [
        "🎉",
        "✨",
        "🔥",
        "🌈",
        "🎈",
        "⭐",
        "💜",
        "🍀",
        "🚀",
        "🎶",
        "🌟",
        "🎯",
    ];

    public static readonly IReadOnlyList<FunItem> Animals =
    [
        new("🦊", "fox"),
        new("🐱", "cat"),
        new("🐶", "dog"),
        new("🐼", "panda"),
        new("🐸", "frog"),
        new("🦉", "owl"),
        new("🐧", "penguin"),
        new("🐙", "octopus"),
        new("🦄", "unicorn"),
        new("🐢", "turtle"),
        new("🦁", "lion"),
        new("🐰", "rabbit"),
    ];

    public static readonly IReadOnlyList<FunItem> Foods =
    [
        new("🍕", "pizza"),
        new("🍔", "burger"),
        new("🌮", "taco"),
        new("🍣", "sushi"),
        new("🍩", "donut"),
        new("🍪", "cookie"),
        new("🍎", "apple"),
        new("🍌", "banana"),
        new("🍓", "strawberry"),
        new("🥑", "avocado"),
        new("🍿", "popcorn"),
        new("🍦", "ice cream"),
    ];

    public static readonly IReadOnlyList<FunItem> Colors =
    [
        new(null, "teal"),
        new(null, "red"),
        new(null, "blue"),
        new(null, "green"),
        new(null, "yellow"),
        new(null, "orange"),
        new(null, "purple"),
        new(null, "pink"),
        new(null, "brown"),
        new(null, "gold"),
        new(null, "silver"),
        new(null, "navy"),
    ];

    /// <summary>Rock, paper, scissors in this order: each one beats the one two places after it, wrapping round.</summary>
    public static readonly IReadOnlyList<FunItem> Hands =
    [
        new("🪨", "rock"),
        new("📄", "paper"),
        new("✂️", "scissors"),
    ];

    public static readonly IReadOnlyList<string> SlotSymbols = ["🍒", "🍋", "🔔", "🍇", "⭐", "7️⃣"];

    /// <summary>The six faces of a die, U+2680 to U+2685.</summary>
    public const string DieFaces = "⚀⚁⚂⚃⚄⚅";

    public static T Pick<T>(IReadOnlyList<T> items, Random random) =>
        items[random.Next(items.Count)];
}
