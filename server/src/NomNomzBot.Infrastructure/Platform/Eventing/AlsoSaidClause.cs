// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
