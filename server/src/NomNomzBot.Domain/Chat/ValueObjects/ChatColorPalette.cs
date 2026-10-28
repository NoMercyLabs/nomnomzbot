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

namespace NomNomzBot.Domain.Chat.ValueObjects;

/// <summary>The 15 chat colors Twitch gives a chatter who has not picked one, in the order ties are broken.</summary>
public static class ChatColorPalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#0000FF",
        "#8A2BE2",
        "#5F9EA0",
        "#D2691E",
        "#FF7F50",
        "#1E90FF",
        "#B22222",
        "#DAA520",
        "#008000",
        "#FF69B4",
        "#FF4500",
        "#FF0000",
        "#2E8B57",
        "#00FF7F",
        "#9ACD32",
    ];

    /// <summary>The Twitch name of each color in <see cref="Colors"/>, at the same index. Update User Chat
    /// Color accepts these names from every user; a hex value needs Turbo or Prime.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "blue",
        "blue_violet",
        "cadet_blue",
        "chocolate",
        "coral",
        "dodger_blue",
        "firebrick",
        "golden_rod",
        "green",
        "hot_pink",
        "orange_red",
        "red",
        "sea_green",
        "spring_green",
        "yellow_green",
    ];
}
