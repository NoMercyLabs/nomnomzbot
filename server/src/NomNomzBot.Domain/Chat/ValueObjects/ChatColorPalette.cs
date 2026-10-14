// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
}
