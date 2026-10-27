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
/// The template keys the registry used to accept as aliases and no longer resolves, each with the canonical
/// key that replaces it. Only the <c>RewriteRetiredTemplateKeys</c> migrations read this map: they rewrite
/// every saved template once, so no alias code stays in the registry. Frozen on purpose. A later key rename
/// gets its own map and its own migration.
/// </summary>
public static class RetiredTemplateKeyMap
{
    /// <summary>The old key (as typed between the braces) and the key that replaces it.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Pairs { get; } =
    [
        new("nothing", "chars.nothing"),
        new("newline", "chars.newline"),
        new("space", "chars.space"),
        new("pipe", "chars.pipe"),
        new("zws", "chars.zws"),
    ];
}
