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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Widgets.Dtos;

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
    /// The widget <c>nnz.d.ts</c> for one widget: <c>settings</c>, <c>onSettings</c> and <c>WIDGET_SETTINGS</c> are
    /// typed as <c>NnzWidgetSettings</c>, one member per field of <paramref name="settings"/>.
    /// </summary>
    string EmitWidgetTypeScript(WidgetSettingsSchema settings);

    /// <summary>
    /// The event catalog for <paramref name="context"/> — one item per visible event: wire name,
    /// tier, and the payload JSON Schema. Ordered by wire name.
    /// </summary>
    IReadOnlyList<EventCatalogItemDto> EmitEventCatalog(SdkContext context);
}
