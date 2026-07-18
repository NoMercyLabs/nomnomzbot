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
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Services;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Reflects a set of visible <see cref="EventDescriptor"/>s into the generated <c>nnz.d.ts</c> (dev-platform.md
/// §2.1): one <c>interface</c> per event payload and per nested value object, the <c>NnzEventMap</c>, and the
/// typed <c>nnz.on&lt;K&gt;</c> surface copied from the <c>nomercy-player-core</c> shape. One instance builds one
/// context's output; it is not reused.
/// </summary>
internal sealed class TypeScriptDefinitionWriter
{
    private readonly SdkContext _context;
    private readonly Dictionary<Type, string> _interfaceNames = new();
    private readonly HashSet<string> _usedNames = new(StringComparer.Ordinal);
    private readonly List<Type> _objectTypes = [];
    private readonly Dictionary<Type, List<string>> _propertyLines = new();
    private readonly Queue<Type> _pending = new();

    public TypeScriptDefinitionWriter(SdkContext context) => _context = context;

    public string Build(IReadOnlyList<EventDescriptor> events)
    {
        // Seed the queue with every event payload type (already ordered by wire name), then drain — walking a
        // property's type registers any nested value object, so the queue discovers the whole reachable graph.
        foreach (EventDescriptor descriptor in events)
            RegisterObject(descriptor.ClrType);

        while (_pending.Count > 0)
        {
            Type type = _pending.Dequeue();
            List<string> lines = [];
            foreach (PropertyInfo property in SdkReflection.ExposedProperties(type, _context))
            {
                string name = SdkReflection.JsonName(property);
                bool nullable = SdkReflection.IsNullable(property);
                string tsType = TsType(property.PropertyType, nullable);
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
            sb.AppendLine($"interface {_interfaceNames[type]} {{");
            foreach (string line in _propertyLines[type])
                sb.AppendLine(line);
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("interface NnzEventMap {");
        foreach (EventDescriptor descriptor in events)
            sb.AppendLine($"  '{descriptor.WireName}': {_interfaceNames[descriptor.ClrType]};");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("declare const nnz: {");
        sb.AppendLine(
            "  on<K extends keyof NnzEventMap>(event: K, fn: (data: NnzEventMap[K]) => void): void;"
        );
        sb.AppendLine(
            "  once<K extends keyof NnzEventMap>(event: K, fn: (data: NnzEventMap[K]) => void): void;"
        );
        sb.AppendLine(
            "  off<K extends keyof NnzEventMap>(event: K, fn?: (data: NnzEventMap[K]) => void): void;"
        );
        sb.AppendLine("};");

        return sb.ToString();
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
