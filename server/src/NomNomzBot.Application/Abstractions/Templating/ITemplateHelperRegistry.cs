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
/// Every helper the engine knows: the static as-built rows of <see cref="TemplateHelperRegistry"/> plus
/// every <see cref="ITemplateHelperProvider"/>'s rows. Drives <c>GET api/v1/templates/helpers</c>, save-time
/// validation and the resolver's dispatch by namespace.
/// </summary>
public interface ITemplateHelperRegistry
{
    IReadOnlyList<TemplateHelperEntry> All { get; }

    /// <summary>The full valid set for one context.</summary>
    IReadOnlyList<TemplateHelperEntry> ForContext(TemplateHelperContext context);

    /// <summary>The valid set for one context, narrowed to what one EventSub event seeds (S-OWN16).</summary>
    IReadOnlyList<TemplateHelperEntry> ForContext(TemplateHelperContext context, string eventType);

    /// <summary>The entry a key (canonical or prefix family member) belongs to, or null.</summary>
    TemplateHelperEntry? Find(string key);

    /// <summary>The provider that owns the key's namespace, or null when the as-built path renders it.</summary>
    ITemplateHelperProvider? ProviderFor(string key);
}
