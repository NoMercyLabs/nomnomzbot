// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// The SDK type emitter (dev-platform.md §1.3, §2, §3.1) — turns the reflected <see cref="IEventCatalog"/> into
/// the per-context <c>nnz.d.ts</c> and event catalog. It only selects the visible tier set for a context and
/// delegates the actual reflection to <see cref="TypeScriptDefinitionWriter"/> / <see cref="JsonSchemaWriter"/>.
/// Pure <c>System.Reflection</c> — no Roslyn. Stateless, so it is registered as a singleton.
/// </summary>
public sealed class SdkTypeEmitter : ISdkTypeEmitter
{
    private readonly IEventCatalog _catalog;

    public SdkTypeEmitter(IEventCatalog catalog) => _catalog = catalog;

    public string EmitTypeScript(SdkContext context) =>
        new TypeScriptDefinitionWriter(context).Build(VisibleFor(context));

    public IReadOnlyList<EventCatalogItemDto> EmitEventCatalog(SdkContext context)
    {
        JsonSchemaWriter schema = new(context);
        return
        [
            .. VisibleFor(context)
                .Select(d => new EventCatalogItemDto(
                    d.WireName,
                    d.Visibility.ToString(),
                    schema.BuildPayloadSchema(d.ClrType)
                )),
        ];
    }

    /// <summary>
    /// The events a context may see (dev-platform.md §1.2/§3.1): the widget surface is
    /// <see cref="EventVisibility.Public"/> only; the script surface admits everything up to
    /// <see cref="EventVisibility.Broadcaster"/>. <see cref="EventVisibility.Internal"/> is never emitted.
    /// </summary>
    private IReadOnlyList<EventDescriptor> VisibleFor(SdkContext context) =>
        [
            .. _catalog.Descriptors.Where(d =>
                context == SdkContext.Widget
                    ? d.Visibility == EventVisibility.Public
                    : d.Visibility != EventVisibility.Internal
            ),
        ];
}
