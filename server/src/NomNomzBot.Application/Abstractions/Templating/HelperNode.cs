// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// One helper call as a provider sees it: the key and the arguments, each already rendered (inside out)
/// and trimmed. <see cref="Raw"/> is the token as written, which the resolver prints when the provider
/// returns null.
/// </summary>
public sealed record HelperNode(string Key, IReadOnlyList<string> Arguments, string Raw)
{
    /// <summary>The key text before the first <c>.</c>: <c>music</c> for <c>music.queue.position</c>.</summary>
    public string Namespace => NamespaceOf(Key);

    public static string NamespaceOf(string key)
    {
        int end = key.IndexOfAny(['.', ':']);
        return end < 0 ? key : key[..end];
    }
}
