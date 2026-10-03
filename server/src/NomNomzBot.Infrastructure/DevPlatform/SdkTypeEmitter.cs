// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Application.Widgets.Dtos;
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
    private readonly ITriggerSampleCatalog? _samples;
    private readonly IReadOnlyList<ICommandAction> _actions;
    private readonly IWidgetEventPayloadRegistry? _widgetEvents;
    private readonly ILogger<SdkTypeEmitter>? _logger;

    public SdkTypeEmitter(
        IEventCatalog catalog,
        ITriggerSampleCatalog? samples = null,
        IEnumerable<ICommandAction>? actions = null,
        IWidgetEventPayloadRegistry? widgetEvents = null,
        ILogger<SdkTypeEmitter>? logger = null
    )
    {
        _widgetEvents = widgetEvents;
        _logger = logger;
        _catalog = catalog;
        _samples = samples;
        _actions = [.. actions ?? []];
    }

    public string EmitTypeScript(SdkContext context) =>
        new TypeScriptDefinitionWriter(context, null, _actions, _logger, _widgetEvents).Build(
            VisibleFor(context)
        );

    public string EmitWidgetTypeScript(WidgetSettingsSchema settings) =>
        new TypeScriptDefinitionWriter(
            SdkContext.Widget,
            null,
            _actions,
            _logger,
            _widgetEvents,
            settings
        ).Build(VisibleFor(SdkContext.Widget));

    public Result<string> EmitTypeScript(SdkContext context, string triggerKey) =>
        EmitTypeScript(context, [triggerKey]);

    public Result<string> EmitTypeScript(SdkContext context, IReadOnlyList<string> triggerKeys)
    {
        if (triggerKeys.Count == 0)
            return Result.Failure<string>("No trigger given.", "UNKNOWN_TRIGGER");

        HashSet<string> union = new(StringComparer.Ordinal);
        foreach (string triggerKey in triggerKeys)
        {
            IReadOnlyList<string>? keys = TriggerVariableKeys(triggerKey);
            if (keys is null)
                return Result.Failure<string>(
                    $"Unknown trigger '{triggerKey}'.",
                    "UNKNOWN_TRIGGER"
                );
            union.UnionWith(keys);
        }

        return Result.Success(
            new TypeScriptDefinitionWriter(
                context,
                [.. union.OrderBy(key => key, StringComparer.Ordinal)],
                _actions,
                _logger,
                _widgetEvents
            ).Build(VisibleFor(context))
        );
    }

    // The keys a trigger always sets: the live chat-command set, or the union over the samples that run the
    // event response. Null when no trigger has that key.
    private IReadOnlyList<string>? TriggerVariableKeys(string triggerKey)
    {
        if (string.Equals(triggerKey, ChatCommandVariableKeys.Trigger, StringComparison.Ordinal))
            return ChatCommandVariableKeys.Keys;

        List<TriggerSample> matching =
        [
            .. (_samples?.List() ?? []).Where(sample =>
                string.Equals(sample.ResponseKey, triggerKey, StringComparison.Ordinal)
            ),
        ];
        if (matching.Count == 0)
            return null;

        return
        [
            .. matching
                .SelectMany(sample => sample.TypeKeys ?? [.. sample.Variables.Keys])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal),
        ];
    }

    public IReadOnlyList<EventCatalogItemDto> EmitEventCatalog(SdkContext context)
    {
        JsonSchemaWriter schema = new(context);
        return
        [
            .. VisibleFor(context)
                .Select(d => new EventCatalogItemDto(
                    d.WireName,
                    d.Visibility.ToString(),
                    schema.BuildPayloadSchema(d.ClrType),
                    EventSamplePayloads.ByWireName.GetValueOrDefault(d.WireName)
                        ?? ReflectionSampleGenerator.Generate(d.ClrType, context)
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
