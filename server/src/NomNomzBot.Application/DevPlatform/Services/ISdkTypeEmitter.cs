// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
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
    /// The script <c>nnz.d.ts</c> for one trigger: <c>bot.getVar</c> accepts only the variable keys that trigger
    /// sets, plus a dynamic overload for keys set elsewhere. <paramref name="triggerKey"/> is an event response
    /// key (<c>channel.follow</c>) or <c>command</c> for a chat command. An unknown trigger is a failure.
    /// </summary>
    Result<string> EmitTypeScript(SdkContext context, string triggerKey);

    /// <summary>
    /// The script <c>nnz.d.ts</c> for a script that several triggers run: <c>NnzVarKey</c> is the union of the keys
    /// of every trigger. One unknown trigger, or an empty list, is a failure, so no wrong narrow type is served.
    /// </summary>
    Result<string> EmitTypeScript(SdkContext context, IReadOnlyList<string> triggerKeys);

    /// <summary>
    /// The event catalog for <paramref name="context"/> — one item per visible event: wire name,
    /// tier, and the payload JSON Schema. Ordered by wire name.
    /// </summary>
    IReadOnlyList<EventCatalogItemDto> EmitEventCatalog(SdkContext context);
}
