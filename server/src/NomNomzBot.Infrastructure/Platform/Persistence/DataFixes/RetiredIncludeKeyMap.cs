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

namespace NomNomzBot.Infrastructure.Platform.Persistence.DataFixes;

/// <summary>
/// The include key that was renamed when <c>{command.*}</c> kept the running-command info (2026-10-27): including
/// another command is <c>{include.cmd:name}</c> now. Only the <c>RewriteIncludeCommandKey</c> migrations read this
/// map, through <see cref="RetiredTemplateKeySql"/>: they rewrite every saved template once, so no alias stays in the
/// registry. Frozen on purpose.
/// </summary>
public static class RetiredIncludeKeyMap
{
    /// <summary>The old key (as typed between the braces) and the key that replaces it.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Pairs { get; } =
    [new("include.command", "include.cmd")];
}
