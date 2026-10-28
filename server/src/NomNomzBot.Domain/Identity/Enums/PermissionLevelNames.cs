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

namespace NomNomzBot.Domain.Identity.Enums;

/// <summary>
/// Converts between a permission rung's NAME — what every DTO carries — and the unified ladder's integer,
/// which is what the database columns and the runtime comparisons use.
///
/// <para>The wire used to carry the raw integer, and the create-command request documented it with a scale
/// the product had already left behind ("4=moderator, 5=broadcaster" against real rungs of 0/2/4/6/10/20/30/40).
/// An integration following that comment asked for broadcaster-only and got a command every subscriber could
/// run. A name cannot drift like that: it either resolves to a rung or it is refused.</para>
/// </summary>
public static class PermissionLevelNames
{
    /// <summary>The rung name for a stored ladder value — the exact rung when it is one, else the highest cleared.</summary>
    public static string ToName(int levelValue) =>
        AuthorizationLadder.FromLevelValue(levelValue).ToString();

    /// <summary>
    /// The ladder value for a rung name, case-insensitively. Null for anything that is not a rung — including
    /// a stringified integer, which is almost always an old client that has not been updated and must be told
    /// so rather than silently interpreted.
    /// </summary>
    public static int? ToLevelValue(string? name) =>
        Enum.TryParse(name, ignoreCase: true, out PermissionLevel level) && Enum.IsDefined(level)
            ? level.ToLevelValue()
            : null;

    /// <summary>Every rung name, ladder order — the closed set a picker offers and validation checks against.</summary>
    public static IReadOnlyList<string> All { get; } =
    [.. Enum.GetValues<PermissionLevel>().OrderBy(l => l.ToLevelValue()).Select(l => l.ToString())];
}
