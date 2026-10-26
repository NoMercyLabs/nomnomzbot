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
/// Owns one template namespace (<c>music</c>, <c>text</c>, …): its registry rows and how they render.
/// A provider is a class; one reflection scan registers every provider, so a new namespace adds a class
/// and edits no shared file. Providers are singletons: one that needs scoped services creates a scope.
/// </summary>
public interface ITemplateHelperProvider
{
    /// <summary>The namespace this provider owns: the key text before the first <c>.</c> or <c>:</c>.</summary>
    string Namespace { get; }

    /// <summary>Its registry rows, with canonical keys and aliases.</summary>
    IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>
    /// Fetches what the nodes need, once per <see cref="TemplateHelperEntry.Source"/> for every node of one
    /// render, and keeps it in <see cref="TemplateRenderScope.Items"/>.
    /// </summary>
    ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    );

    /// <summary>The rendered text of one node; null renders the token as written.</summary>
    ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    );
}
