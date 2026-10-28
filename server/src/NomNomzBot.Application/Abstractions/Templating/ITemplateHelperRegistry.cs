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
