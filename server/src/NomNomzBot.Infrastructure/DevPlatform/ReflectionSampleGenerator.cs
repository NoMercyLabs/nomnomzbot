// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NomNomzBot.Application.DevPlatform;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// Generates a plausible sample payload for an event that has no external wire format to translate from — an
/// internal domain event NomNomzBot itself raises (dev-platform.md §1.3). Unlike <see cref="EventSamplePayloads"/>
/// (real Twitch-wire fixtures copied verbatim from translator tests), there is nothing to copy here: the C# type
/// definition on <c>NomNomzBot.Domain</c> IS the ground truth for these events, so a reflection-generated instance
/// of that same type — walked with the identical <see cref="SdkReflection"/> exposure rules the JSON Schema
/// (<see cref="JsonSchemaWriter"/>) is built from — cannot drift or misrepresent the contract. One plausible value
/// is produced per property based on its declared type, using the property name for context where sensible (e.g.
/// a property named <c>Username</c> gets a realistic-looking username, not a placeholder like "string1").
/// </summary>
internal sealed class ReflectionSampleGenerator
{
    private readonly SdkContext _context;

    private ReflectionSampleGenerator(SdkContext context) => _context = context;

    /// <summary>Builds the sample payload for <paramref name="type"/> and serializes it to indented JSON.</summary>
    public static string Generate(Type type, SdkContext context)
    {
        JsonObject sample = new ReflectionSampleGenerator(context).BuildObjectSample(
            type,
            new HashSet<Type>()
        );
        return sample.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private JsonObject BuildObjectSample(Type type, IReadOnlySet<Type> ancestors)
    {
        HashSet<Type> branch = [.. ancestors, type];

        JsonObject sample = new();
        foreach (PropertyInfo property in SdkReflection.ExposedProperties(type, _context))
        {
            string name = SdkReflection.JsonName(property);
            sample[name] = ValueFor(property.PropertyType, property.Name, branch);
        }
        return sample;
    }

    private JsonNode? ValueFor(Type type, string propertyName, IReadOnlySet<Type> ancestors)
    {
        SdkReflection.ClassifiedType classified = SdkReflection.Classify(type);
        return classified.Category switch
        {
            SdkReflection.TypeCategory.StringLike => StringValue(
                classified.Underlying,
                propertyName
            ),
            SdkReflection.TypeCategory.IntegerLike => JsonValue.Create(IntegerValue(propertyName)),
            SdkReflection.TypeCategory.NumberLike => JsonValue.Create(NumberValue(propertyName)),
            SdkReflection.TypeCategory.BoolLike => JsonValue.Create(true),
            SdkReflection.TypeCategory.Enum => JsonValue.Create(EnumValue(classified.Underlying)),
            SdkReflection.TypeCategory.Collection => new JsonArray(
                ValueFor(classified.ElementType!, propertyName, ancestors)
            ),
            SdkReflection.TypeCategory.Dictionary => new JsonObject
            {
                ["sampleKey"] = ValueFor(classified.DictValueType!, propertyName, ancestors),
            },
            SdkReflection.TypeCategory.Object => ancestors.Contains(classified.Underlying)
                ? new JsonObject()
                : BuildObjectSample(classified.Underlying, ancestors),
            _ => null,
        };
    }

    private static JsonValue StringValue(Type underlying, string propertyName)
    {
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
            return JsonValue.Create("2026-06-20T11:29:00Z")!;
        if (underlying == typeof(DateOnly))
            return JsonValue.Create("2026-06-20")!;
        if (underlying == typeof(TimeOnly))
            return JsonValue.Create("11:29:00")!;
        if (underlying == typeof(TimeSpan))
            return JsonValue.Create("00:05:00")!;
        if (underlying == typeof(Guid))
            return JsonValue.Create("11111111-2222-3333-4444-555555555555")!;
        if (underlying.Name == "Ulid")
            return JsonValue.Create("01ARZ3NDEKTSV4RRFFQ69G5FAV")!;

        string lower = propertyName.ToLowerInvariant();
        if (lower.Contains("email", StringComparison.Ordinal))
            return JsonValue.Create("viewer@example.com")!;
        if (
            lower.Contains("username", StringComparison.Ordinal)
            || lower.EndsWith("login", StringComparison.Ordinal)
        )
            return JsonValue.Create("cool_user")!;
        if (
            lower.Contains("color", StringComparison.Ordinal)
            || lower.Contains("colour", StringComparison.Ordinal)
        )
            return JsonValue.Create("#FF6B35")!;
        if (
            lower.Contains("url", StringComparison.Ordinal)
            || lower.Contains("uri", StringComparison.Ordinal)
            || lower.Contains("link", StringComparison.Ordinal)
            || lower.Contains("website", StringComparison.Ordinal)
            || lower.Contains("logo", StringComparison.Ordinal)
        )
            return JsonValue.Create("https://example.com/resource")!;
        if (lower.Contains("reason", StringComparison.Ordinal))
            return JsonValue.Create("sample reason")!;
        if (
            lower.Contains("message", StringComparison.Ordinal)
            || lower.Contains("text", StringComparison.Ordinal)
            || lower.Contains("description", StringComparison.Ordinal)
            || lower.Contains("prompt", StringComparison.Ordinal)
        )
            return JsonValue.Create($"Sample {SplitWords(propertyName)}")!;
        if (
            lower.Contains("title", StringComparison.Ordinal)
            || lower.Contains("name", StringComparison.Ordinal)
        )
            return JsonValue.Create($"Sample {SplitWords(propertyName)}")!;
        if (lower.Contains("code", StringComparison.Ordinal))
            return JsonValue.Create("ABC123")!;
        if (
            lower.Contains("status", StringComparison.Ordinal)
            || lower.Contains("state", StringComparison.Ordinal)
        )
            return JsonValue.Create("active")!;
        if (lower.EndsWith("id", StringComparison.Ordinal))
            return JsonValue.Create(
                $"id-{Math.Abs(propertyName.GetHashCode(StringComparison.Ordinal)) % 100_000}"
            )!;

        return JsonValue.Create(
            $"sample-{SplitWords(propertyName).Replace(' ', '-').ToLowerInvariant()}"
        )!;
    }

    private static long IntegerValue(string propertyName)
    {
        string lower = propertyName.ToLowerInvariant();
        if (lower.Contains("count", StringComparison.Ordinal))
            return 3;
        if (
            lower.Contains("duration", StringComparison.Ordinal)
            || lower.Contains("seconds", StringComparison.Ordinal)
            || lower.Contains("minutes", StringComparison.Ordinal)
        )
            return 30;
        if (
            lower.Contains("amount", StringComparison.Ordinal)
            || lower.Contains("cost", StringComparison.Ordinal)
            || lower.Contains("total", StringComparison.Ordinal)
            || lower.Contains("bits", StringComparison.Ordinal)
            || lower.Contains("points", StringComparison.Ordinal)
            || lower.Contains("balance", StringComparison.Ordinal)
            || lower.Contains("cents", StringComparison.Ordinal)
        )
            return 100;
        if (lower.Contains("level", StringComparison.Ordinal))
            return 2;
        return 42;
    }

    private static double NumberValue(string propertyName)
    {
        string lower = propertyName.ToLowerInvariant();
        if (
            lower.Contains("amount", StringComparison.Ordinal)
            || lower.Contains("price", StringComparison.Ordinal)
        )
            return 99.99;
        return 42.5;
    }

    private static string EnumValue(Type enumType) =>
        Enum.GetNames(enumType).FirstOrDefault() ?? "unknown";

    private static string SplitWords(string pascalCaseName)
    {
        System.Text.StringBuilder sb = new();
        foreach (char c in pascalCaseName)
        {
            if (sb.Length > 0 && char.IsUpper(c))
                sb.Append(' ');
            sb.Append(c);
        }
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(sb.ToString());
    }
}
