// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Reads the <c>settings.json</c> a self-authored widget carries at its project root and turns it into the typed
/// fields the dashboard's generic settings form renders. Labels, help and groups are the author's literal text
/// (the dashboard shows an unknown translation key as written). Every failure names the field and the rule.
/// </summary>
public static partial class CustomWidgetSettingsDeclaration
{
    public const string FileName = "settings.json";

    public const string InvalidCode = "WIDGET_SETTINGS_INVALID";

    private const string DefaultGroup = "Settings";

    private static readonly HashSet<string> Types =
    [
        "bool",
        "number",
        "text",
        "color",
        "select",
        "multiselect",
        "json",
    ];

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex KeyPattern();

    public static Result<IReadOnlyList<WidgetSettingsField>> Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Fail($"{FileName} is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("fields", out JsonElement array)
                || array.ValueKind != JsonValueKind.Array
            )
                return Fail($"{FileName} must be an object with a \"fields\" array.");

            List<WidgetSettingsField> fields = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            int index = 0;
            foreach (JsonElement element in array.EnumerateArray())
            {
                Result<WidgetSettingsField> field = ParseField(element, index++, seen);
                if (field.IsFailure)
                    return Fail(field.ErrorMessage!);
                fields.Add(field.Value);
            }

            return Result.Success<IReadOnlyList<WidgetSettingsField>>(fields);
        }
    }

    private static Result<IReadOnlyList<WidgetSettingsField>> Fail(string message) =>
        Result.Failure<IReadOnlyList<WidgetSettingsField>>(message, InvalidCode);

    private static Result<WidgetSettingsField> ParseField(
        JsonElement element,
        int index,
        HashSet<string> seen
    )
    {
        if (element.ValueKind != JsonValueKind.Object)
            return Bad($"field {index}", "must be an object.");

        string? key = Text(element, "key");
        if (string.IsNullOrEmpty(key))
            return Bad($"field {index}", "needs a non-empty \"key\".");

        string name = $"field \"{key}\"";
        if (!KeyPattern().IsMatch(key))
            return Bad(
                name,
                "key must start with a letter and contain only letters, digits and underscores."
            );
        if (!seen.Add(key))
            return Bad(name, "key is used twice; every key must be unique.");

        string? type = Text(element, "type");
        if (type is null || !Types.Contains(type))
            return Bad(
                name,
                $"type \"{type}\" is not allowed; use one of: {string.Join(", ", Types.Order())}."
            );

        List<WidgetSettingsFieldOption>? options = null;
        if (type is "select" or "multiselect")
        {
            options = ParseOptions(element);
            if (options.Count == 0)
                return Bad(name, $"a {type} field needs at least one option.");
        }

        element.TryGetProperty("default", out JsonElement defaultElement);
        Result<object?> defaultValue = ParseDefault(type, defaultElement, options);
        if (defaultValue.IsFailure)
            return Bad(name, defaultValue.ErrorMessage!);

        double? min = Number(element, "min");
        double? max = Number(element, "max");
        if (min is not null && max is not null && min > max)
            return Bad(name, "min must not be greater than max.");

        string? help = Text(element, "help");
        return Result.Success(
            new WidgetSettingsField(
                key,
                new(Text(element, "label") ?? key),
                type,
                new(Text(element, "group") ?? DefaultGroup),
                defaultValue.Value,
                help is null ? null : new LocalizedText(help),
                options,
                min,
                max,
                Number(element, "step")
            )
        );
    }

    private static Result<WidgetSettingsField> Bad(string name, string rule) =>
        Result.Failure<WidgetSettingsField>($"{name}: {rule}", InvalidCode);

    private static List<WidgetSettingsFieldOption> ParseOptions(JsonElement field)
    {
        List<WidgetSettingsFieldOption> options = [];
        if (
            !field.TryGetProperty("options", out JsonElement array)
            || array.ValueKind != JsonValueKind.Array
        )
            return options;

        foreach (JsonElement option in array.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object)
                continue;
            string? value = Text(option, "value");
            if (value is not null)
                options.Add(new(value, new(Text(option, "label") ?? value)));
        }

        return options;
    }

    private static Result<object?> ParseDefault(
        string type,
        JsonElement value,
        List<WidgetSettingsFieldOption>? options
    )
    {
        bool present = value.ValueKind != JsonValueKind.Undefined;
        switch (type)
        {
            case "bool":
                return present && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? Result.Success<object?>(value.GetBoolean())
                    : Result.Failure<object?>("default must be true or false for a bool field.");
            case "number":
                return present && value.ValueKind == JsonValueKind.Number
                    ? Result.Success<object?>(value.GetDouble())
                    : Result.Failure<object?>("default must be a number for a number field.");
            case "select":
                string? chosen =
                    present && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                return chosen is not null && options!.Any(o => o.Value == chosen)
                    ? Result.Success<object?>(chosen)
                    : Result.Failure<object?>("default must be the value of one of its options.");
            case "multiselect":
                return ParseMultiDefault(value, options!);
            default:
                return present && value.ValueKind == JsonValueKind.String
                    ? Result.Success<object?>(value.GetString())
                    : Result.Failure<object?>($"default must be a string for a {type} field.");
        }
    }

    private static Result<object?> ParseMultiDefault(
        JsonElement value,
        List<WidgetSettingsFieldOption> options
    )
    {
        const string rule = "default must be an array of option values for a multiselect field.";
        if (value.ValueKind != JsonValueKind.Array)
            return Result.Failure<object?>(rule);

        List<string> chosen = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (text is null || !options.Any(o => o.Value == text))
                return Result.Failure<object?>(rule);
            chosen.Add(text);
        }

        return Result.Success<object?>(chosen);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
