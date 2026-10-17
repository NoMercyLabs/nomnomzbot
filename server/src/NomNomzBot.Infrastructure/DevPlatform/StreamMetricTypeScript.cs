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
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Stats;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Writes the stream-stats types both SDK contexts share: <c>NnzStreamMetricMap</c> (metric key to result type),
/// reflected from every <see cref="IStreamMetric"/> class, plus the window and the <c>NnzStreamStats&lt;K&gt;</c>
/// answer of <c>streamStats</c>. A new metric class extends the map here without touching this file.
/// </summary>
internal static class StreamMetricTypeScript
{
    /// <summary>Every registered metric: its key and the C# type it computes, in key order.</summary>
    public static IReadOnlyList<(string Key, Type Result)> Metrics() =>
        typeof(StreamMetricTypeScript)
            .Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true })
            .Where(t => typeof(IStreamMetric).IsAssignableFrom(t))
            .Select(t => (KeyOf(t), ResultOf(t)))
            .OrderBy(m => m.Item1, StringComparer.Ordinal)
            .ToList();

    public static string Write()
    {
        IReadOnlyList<(string Key, Type Result)> metrics = Metrics();
        List<Type> records = [typeof(StreamStatsWindowDto)];
        foreach ((string _, Type result) in metrics)
            CollectRecords(result, records);

        StringBuilder sb = new();
        foreach (Type record in records)
            AppendRecord(sb, record);

        sb.AppendLine("/** Each stream metric key and the value it returns. */");
        sb.AppendLine("interface NnzStreamMetricMap {");
        foreach ((string key, Type result) in metrics)
        {
            string optional = Nullable.GetUnderlyingType(result) is null ? "" : "?";
            AppendDoc(sb, DocOfMetric(key));
            sb.AppendLine($"  {key}{optional}: {TypeOf(result)};");
        }
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("type NnzStreamMetricKey = keyof NnzStreamMetricMap;");
        sb.AppendLine();
        sb.AppendLine(
            "/** The answer of streamStats: the stream window plus the requested metrics. An optional metric (?) is absent when it has nothing to show. */"
        );
        sb.AppendLine(
            "type NnzStreamStats<K extends NnzStreamMetricKey> = Pick<NnzStreamMetricMap, K> & { stream: NnzStreamWindow };"
        );
        sb.AppendLine();
        sb.AppendLine(
            "/** What to read: the stream (the latest when left out) and the metric keys. */"
        );
        sb.AppendLine("interface NnzStreamStatsOptions<K extends NnzStreamMetricKey> {");
        sb.AppendLine("  /** The stream id. Leave out for the latest stream, live or ended. */");
        sb.AppendLine("  stream?: string;");
        sb.AppendLine("  /** The metric keys to read. */");
        sb.AppendLine("  metrics: readonly K[];");
        sb.AppendLine("}");
        sb.AppendLine();
        return sb.ToString();
    }

    private static readonly XmlDocSummaryReader? Docs = XmlDocSummaryReader.ForAssembly(
        typeof(StreamStatsDto).Assembly,
        null
    );

    private static void AppendDoc(StringBuilder sb, string? doc)
    {
        if (doc is not null)
            sb.AppendLine($"  /** {doc} */");
    }

    // A metric key is the wire name of one StreamStatsDto property; that property's summary documents the key.
    private static string? DocOfMetric(string key)
    {
        PropertyInfo? property = typeof(StreamStatsDto)
            .GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == key);
        return property is null ? null : Docs?.PropertySummary(property);
    }

    private static string KeyOf(Type metric) =>
        ((IStreamMetric)RuntimeHelpers.GetUninitializedObject(metric)).Key;

    private static Type ResultOf(Type metric)
    {
        Type? type = metric;
        while (type is not null)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(StreamMetric<>))
                return type.GetGenericArguments()[0];
            type = type.BaseType;
        }
        throw new InvalidOperationException(
            $"{metric.Name} must derive from StreamMetric<TResult> so its result type is known."
        );
    }

    private static void CollectRecords(Type type, List<Type> records)
    {
        Type inner = Nullable.GetUnderlyingType(type) ?? type;
        if (ListElement(inner) is { } element)
        {
            CollectRecords(element, records);
            return;
        }
        if (!IsRecord(inner) || records.Contains(inner))
            return;
        records.Add(inner);
        foreach (PropertyInfo property in inner.GetProperties())
            CollectRecords(property.PropertyType, records);
    }

    private static void AppendRecord(StringBuilder sb, Type record)
    {
        NullabilityInfoContext nullability = new();
        if (Docs?.TypeSummary(record) is { } typeDoc)
            sb.AppendLine($"/** {typeDoc} */");
        sb.AppendLine($"interface {InterfaceName(record)} {{");
        foreach (PropertyInfo property in record.GetProperties())
        {
            AppendDoc(sb, Docs?.PropertySummary(property));
            string name =
                property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
            bool nullable = nullability.Create(property).ReadState == NullabilityState.Nullable;
            string suffix = nullable ? " | null" : "";
            sb.AppendLine($"  {name}: {TypeOf(property.PropertyType)}{suffix};");
        }
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static string TypeOf(Type type)
    {
        Type inner = Nullable.GetUnderlyingType(type) ?? type;
        if (ListElement(inner) is { } element)
            return $"{TypeOf(element)}[]";
        if (inner == typeof(string) || inner == typeof(DateTimeOffset))
            return "string";
        if (inner == typeof(bool))
            return "boolean";
        if (inner == typeof(int) || inner == typeof(long) || inner == typeof(double))
            return "number";
        if (IsRecord(inner))
            return InterfaceName(inner);
        throw new InvalidOperationException(
            $"Stream metric type {inner.Name} has no TypeScript mapping; add one here."
        );
    }

    private static Type? ListElement(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            ? type.GetGenericArguments()[0]
            : null;

    private static bool IsRecord(Type type) =>
        type.IsClass
        && type != typeof(string)
        && type.Namespace == typeof(StreamStatsDto).Namespace;

    // StreamStatsRankedDto -> NnzStreamRanked; StreamStatsWindowDto -> NnzStreamWindow.
    private static string InterfaceName(Type record) =>
        "NnzStream" + record.Name.Replace("StreamStats", "").Replace("Dto", "");
}
