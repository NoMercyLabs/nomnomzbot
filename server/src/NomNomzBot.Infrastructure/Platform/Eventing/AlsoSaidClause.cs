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

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The <c>{also_said}</c> template variable: a ready-made clause that quotes what the viewer wrote with their
/// event, or an empty string when they wrote nothing. The template engine has no <c>{{#if}}</c> block, so an
/// optional fragment is carried as a variable that already resolves to the full clause (it starts with a space,
/// so it always closes the line). Wording matches the old bot's "They also said: ..." convention.
/// </summary>
internal static class AlsoSaidClause
{
    public static string From(string? message) =>
        string.IsNullOrWhiteSpace(message) ? string.Empty : $" They also said: \"{message}\"";
}
