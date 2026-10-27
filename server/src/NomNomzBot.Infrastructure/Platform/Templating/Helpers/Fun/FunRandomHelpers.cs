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

/// <summary>The random picks from a fixed safe set: <c>fun.random.emoji</c>, <c>.animal</c>, <c>.food</c> and <c>.color.name</c>.</summary>
internal sealed class FunRandomHelpers()
    : FunHelperGroup(
        FunRow.Pure(
            "fun.random.emoji",
            "🎉",
            call => call.Args.Count == 0 ? FunCatalog.Pick(FunCatalog.Emoji, call.Random) : null
        ),
        FunRow.Pure("fun.random.animal", "🦊 fox", call => Label(call, FunCatalog.Animals)),
        FunRow.Pure("fun.random.food", "🍕 pizza", call => Label(call, FunCatalog.Foods)),
        FunRow.Pure("fun.random.color.name", "teal", call => Label(call, FunCatalog.Colors))
    )
{
    private static string? Label(FunCall call, IReadOnlyList<FunItem> items) =>
        call.Args.Count == 0 ? FunCatalog.Pick(items, call.Random).Label : null;
}
