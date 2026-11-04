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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Logic;

/// <summary>Weights for the pick namespace's weighted selection cell.</summary>
internal static class PickSurfaceFixture
{
    public static IEnumerable<string> ShapesFor(string key) =>
        key == "pick.weighted" ? [":1=rare|9=common"] : [];
}
