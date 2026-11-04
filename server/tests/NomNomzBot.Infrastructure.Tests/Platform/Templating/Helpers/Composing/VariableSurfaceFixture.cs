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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Composing;

/// <summary>A valid assignment: the set helper changes render scope and emits empty text.</summary>
internal static class VariableSurfaceFixture
{
    public static IEnumerable<string> ShapesFor(string key) =>
        key == "set" ? [":matrix_value=5"] : [];
}
