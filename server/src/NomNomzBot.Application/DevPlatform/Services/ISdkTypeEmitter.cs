// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.DevPlatform.Dtos;

namespace NomNomzBot.Application.DevPlatform.Services;

/// <summary>
/// Reflects the Event Catalog's record graph into the SDK type artifacts (dev-platform.md §1.3, §2). Pure
/// <c>System.Reflection</c> — no Roslyn (project rule). Because the records are the source, the emitted TS, the
/// JSON schema, and the runtime projection cannot drift from the C# declarations.
/// </summary>
public interface ISdkTypeEmitter
{
    /// <summary>
    /// The generated TypeScript declaration file (<c>nnz.d.ts</c>) for <paramref name="context"/> — the
    /// <c>NnzEventMap</c> interface, every payload interface, and the typed <c>nnz.on&lt;K&gt;</c> declaration
    /// (the <c>nomercy-player-core</c> shape, dev-platform.md §2.1). The tier set and PII handling follow the
    /// context.
    /// </summary>
    string EmitTypeScript(SdkContext context);

    /// <summary>
    /// The event catalog for <paramref name="context"/> — one item per visible event: wire name,
    /// tier, and the payload JSON Schema. Ordered by wire name.
    /// </summary>
    IReadOnlyList<EventCatalogItemDto> EmitEventCatalog(SdkContext context);
}
