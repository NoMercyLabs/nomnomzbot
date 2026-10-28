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

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// One helper call as a provider sees it: the key and the arguments, each already rendered (inside out)
/// and trimmed. <see cref="Raw"/> is the token as written, which the resolver prints when the provider
/// returns null. For a lazy entry (<see cref="TemplateHelperEntry.LazyArguments"/>) the arguments are the
/// raw argument text instead, and <see cref="RenderArgumentAsync"/> renders one on demand.
/// </summary>
public sealed record HelperNode(string Key, IReadOnlyList<string> Arguments, string Raw)
{
    /// <summary>
    /// Renders argument <c>i</c> (nested tokens included) with the resolver's own evaluator; set only for a
    /// lazy entry, null otherwise. An argument that is never asked for never resolves.
    /// </summary>
    public Func<int, CancellationToken, ValueTask<string>>? RenderArgumentAsync { get; init; }

    /// <summary>The key text before the first <c>.</c>: <c>music</c> for <c>music.queue.position</c>.</summary>
    public string Namespace => NamespaceOf(Key);

    public static string NamespaceOf(string key)
    {
        int end = key.IndexOfAny(['.', ':']);
        return end < 0 ? key : key[..end];
    }
}
