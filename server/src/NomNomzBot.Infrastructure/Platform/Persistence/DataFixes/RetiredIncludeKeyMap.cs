// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
