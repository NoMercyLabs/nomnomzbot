// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Services;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Reflects a set of visible <see cref="EventDescriptor"/>s into the generated <c>nnz.d.ts</c> (dev-platform.md
/// §2.1): one <c>interface</c> per event payload and per nested value object, plus the <c>NnzEventMap</c>. The
/// context's fixed globals come from <see cref="SdkRuntimeSurface"/> — the script sandbox's <c>bot</c>/<c>nnz</c>,
/// or the widget page's <c>NomNomz</c>/<c>WIDGET_*</c>. One instance builds one context's output; it is not reused.
/// </summary>
internal sealed class TypeScriptDefinitionWriter
{
    private readonly SdkContext _context;
    private readonly IReadOnlyList<string>? _triggerKeys;
    private readonly IReadOnlyList<ICommandAction> _actions;
    private readonly Dictionary<Type, string> _interfaceNames = new();
    private readonly HashSet<string> _usedNames = new(StringComparer.Ordinal);
    private readonly List<Type> _objectTypes = [];
    private readonly Dictionary<Type, List<string>> _propertyLines = new();
    private readonly Queue<Type> _pending = new();
    private readonly ILogger? _logger;
    private readonly IWidgetEventPayloadRegistry? _widgetEvents;

    public TypeScriptDefinitionWriter(
        SdkContext context,
        IReadOnlyList<string>? triggerKeys = null,
        IReadOnlyList<ICommandAction>? actions = null,
        ILogger? logger = null,
        IWidgetEventPayloadRegistry? widgetEvents = null
    )
    {
        _widgetEvents = context == SdkContext.Widget ? widgetEvents : null;
        _logger = logger;
        _context = context;
        _triggerKeys = triggerKeys;
        _actions = actions ?? [];
    }

    public string Build(IReadOnlyList<EventDescriptor> events)
    {
        // Seed the queue with every event payload type (already ordered by wire name), then drain — walking a
        // property's type registers any nested value object, so the queue discovers the whole reachable graph.
        foreach (EventDescriptor descriptor in events)
            RegisterObject(descriptor.ClrType);

        // The widget's own event payloads (the broadcasters' records), so NomNomz.on(...) hands out real members.
        if (_widgetEvents is not null)
        {
            foreach (WidgetEventPayloadEntry entry in _widgetEvents.Events)
                if (entry.PayloadType is not null)
                    RegisterObject(entry.PayloadType);
            RegisterObject(_widgetEvents.CustomEventPayloadType);
        }

        while (_pending.Count > 0)
        {
            Type type = _pending.Dequeue();
            List<string> lines = [];
            foreach (PropertyInfo property in SdkReflection.ExposedProperties(type, _context))
            {
                string name = SdkReflection.JsonName(property);
                bool nullable = SdkReflection.IsNullable(property);
                string tsType = TsType(property.PropertyType, nullable);
                string? doc = SummaryOf(property.DeclaringType, r => r.PropertySummary(property));
                if (doc is not null)
                    lines.Add($"  /** {doc} */");
                lines.Add($"  {name}{(nullable ? "?" : string.Empty)}: {tsType};");
            }
            _propertyLines[type] = lines;
        }

        return Render(events);
    }

    private string Render(IReadOnlyList<EventDescriptor> events)
    {
        StringBuilder sb = new();
        sb.AppendLine(
            "// nnz.d.ts — generated from the NomNomzBot Event Catalog (dev-platform.md §2). DO NOT EDIT."
        );
        sb.AppendLine($"// context: {_context.ToString().ToLowerInvariant()}");
        sb.AppendLine("// SPDX-License-Identifier: AGPL-3.0-or-later");
        sb.AppendLine();

        foreach (Type type in _objectTypes)
        {
            string? doc = SummaryOf(type, r => r.TypeSummary(type));
            if (doc is not null)
                sb.AppendLine($"/** {doc} */");
            sb.AppendLine($"interface {_interfaceNames[type]} {{");
            foreach (string line in _propertyLines[type])
                sb.AppendLine(line);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // The fixed nnz.api.* payload interfaces — authored, not reflected (dev-platform.md §3.1), and script-only
        // because a widget page has no capability broker to return them.
        if (_context == SdkContext.Script)
        {
            sb.AppendLine(SdkRuntimeSurface.ScriptApiInterfaces());
            sb.AppendLine();
        }

        // A script has no event bus, so only the widget page gets the event map.
        if (_context == SdkContext.Widget)
        {
            sb.AppendLine("interface NnzEventMap {");
            foreach (EventDescriptor descriptor in events)
                sb.AppendLine($"  '{descriptor.WireName}': {_interfaceNames[descriptor.ClrType]};");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        // Both contexts reach the same pipeline actions through invoke(), so both get the typed parameter map.
        sb.AppendLine(ActionParamsTypeWriter.Write(_actions));

        string? customPayloadName = RenderWidgetEventMap(sb);
        sb.AppendLine(
            _context == SdkContext.Script
                ? SdkRuntimeSurface.ScriptGlobals(_triggerKeys)
                : SdkRuntimeSurface.WidgetGlobals(customPayloadName)
        );

        return sb.ToString();
    }

    // Writes NnzWidgetEventMap (event name -> payload interface) and the onAny union; returns the custom-event
    // payload interface name WidgetGlobals needs, or null when no registry is wired.
    private string? RenderWidgetEventMap(StringBuilder sb)
    {
        if (_widgetEvents is null)
            return null;

        sb.AppendLine(
            "/** Every event a widget can receive, by the name it passes to NomNomz.on(...). */"
        );
        sb.AppendLine("interface NnzWidgetEventMap {");
        foreach (WidgetEventPayloadEntry entry in _widgetEvents.Events)
            sb.AppendLine($"  '{entry.Name}': {WidgetPayloadName(entry.PayloadType)};");
        sb.AppendLine("}");
        sb.AppendLine();

        string customName = _interfaceNames[_widgetEvents.CustomEventPayloadType];
        sb.AppendLine(
            "/** The arguments of an onAny handler: the event name, and the payload that name carries. */"
        );
        sb.AppendLine("type NnzWidgetAnyEvent =");
        foreach (WidgetEventPayloadEntry entry in _widgetEvents.Events)
            sb.AppendLine(
                $"  | [eventType: '{entry.Name}', data: {WidgetPayloadName(entry.PayloadType)}]"
            );
        sb.AppendLine($"  | [eventType: `custom.${{string}}`, data: {customName}];");
        sb.AppendLine();

        return customName;
    }

    private string WidgetPayloadName(Type? payloadType) =>
        payloadType is null ? "Record<string, unknown>" : _interfaceNames[payloadType];

    private string? SummaryOf(Type? owner, Func<XmlDocSummaryReader, string?> read)
    {
        if (owner is null)
            return null;
        XmlDocSummaryReader? reader = XmlDocSummaryReader.ForAssembly(owner.Assembly, _logger);
        return reader is null ? null : read(reader);
    }

    private string TsType(Type type, bool nullable)
    {
        SdkReflection.ClassifiedType classified = SdkReflection.Classify(type);
        string core = classified.Category switch
        {
            SdkReflection.TypeCategory.StringLike => "string",
            SdkReflection.TypeCategory.IntegerLike or SdkReflection.TypeCategory.NumberLike =>
                "number",
            SdkReflection.TypeCategory.BoolLike => "boolean",
            SdkReflection.TypeCategory.Enum => EnumUnion(classified.Underlying, nullable),
            SdkReflection.TypeCategory.Collection => $"{TsType(classified.ElementType!, false)}[]",
            SdkReflection.TypeCategory.Dictionary =>
                $"Record<string, {TsType(classified.DictValueType!, false)}>",
            SdkReflection.TypeCategory.Object => RegisterObject(classified.Underlying),
            _ => "unknown",
        };

        // The enum union already wraps itself for nullability; every other core is a single token.
        if (classified.Category == SdkReflection.TypeCategory.Enum)
            return core;
        return nullable ? $"{core} | null" : core;
    }

    private static string EnumUnion(Type enumType, bool nullable)
    {
        string union = string.Join(" | ", Enum.GetNames(enumType).Select(n => $"'{n}'"));
        if (union.Length == 0)
            union = "string";
        return nullable ? $"({union}) | null" : union;
    }

    private string RegisterObject(Type type)
    {
        if (_interfaceNames.TryGetValue(type, out string? existing))
            return existing;

        string name = UniqueName(type);
        _interfaceNames[type] = name;
        _objectTypes.Add(type);
        _pending.Enqueue(type);
        return name;
    }

    private string UniqueName(Type type)
    {
        string bare = type.Name;
        if (bare.EndsWith("Event", StringComparison.Ordinal) && bare.Length > "Event".Length)
            bare = bare[..^"Event".Length];

        string baseName = $"Nnz{bare}";
        string candidate = baseName;
        int suffix = 2;
        while (!_usedNames.Add(candidate))
        {
            candidate = $"{baseName}{suffix}";
            suffix++;
        }
        return candidate;
    }
}
