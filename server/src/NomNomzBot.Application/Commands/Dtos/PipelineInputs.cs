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

using System.Text.Json;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Dtos;

public sealed record PipelineInputDefinition(
    string Name,
    string Type,
    bool Required,
    JsonElement? DefaultValue,
    string Description
);

public static class PipelineInputs
{
    private static readonly HashSet<string> Types = ["text", "number", "boolean", "list", "object"];

    public static IReadOnlyList<PipelineInputDefinition> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            if (document.RootElement.GetArrayLength() == 0)
                return [];
            if (document.RootElement[0].ValueKind == JsonValueKind.String)
                return
                [
                    .. document
                        .RootElement.EnumerateArray()
                        .Select(item => new PipelineInputDefinition(
                            item.GetString() ?? "",
                            "text",
                            false,
                            null,
                            ""
                        )),
                ];
            return JsonSerializer.Deserialize<List<PipelineInputDefinition>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // The declarations come from request JSON, which can hold a null item.
    public static Result ValidateDeclaration(IReadOnlyList<PipelineInputDefinition?> definitions)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (PipelineInputDefinition? definition in definitions)
        {
            if (definition is null)
                return Result.Failure(
                    "A pipeline input declaration is missing.",
                    "VALIDATION_FAILED"
                );
            if (
                string.IsNullOrWhiteSpace(definition.Name)
                || !char.IsLetter(definition.Name[0])
                || definition.Name.Any(character =>
                    !char.IsLetterOrDigit(character) && character != '_'
                )
                || definition.Name.Length > 100
            )
                return Result.Failure(
                    $"Invalid pipeline input name '{definition.Name}'.",
                    "VALIDATION_FAILED"
                );
            if (!names.Add(definition.Name))
                return Result.Failure(
                    $"Duplicate pipeline input '{definition.Name}'.",
                    "VALIDATION_FAILED"
                );
            if (!Types.Contains(definition.Type))
                return Result.Failure(
                    $"Pipeline input '{definition.Name}' has an invalid type.",
                    "VALIDATION_FAILED"
                );
            if (definition.Description is not { Length: <= 200 })
                return Result.Failure(
                    $"Pipeline input '{definition.Name}' needs a description of at most 200 characters.",
                    "VALIDATION_FAILED"
                );
            if (definition is { Required: true, DefaultValue: not null })
                return Result.Failure(
                    $"Required pipeline input '{definition.Name}' cannot have a default.",
                    "VALIDATION_FAILED"
                );
            if (
                definition.DefaultValue is { } defaultValue
                && !Matches(definition.Type, defaultValue)
            )
                return Result.Failure(
                    $"Default for pipeline input '{definition.Name}' must be {definition.Type}.",
                    "VALIDATION_FAILED"
                );
        }
        return Result.Success();
    }

    public static Result<Dictionary<string, string>> Bind(
        IReadOnlyList<PipelineInputDefinition> definitions,
        IReadOnlyDictionary<string, JsonElement>? supplied
    )
    {
        Result valid = ValidateDeclaration(definitions);
        if (valid.IsFailure)
            return Result.Failure<Dictionary<string, string>>(
                valid.ErrorMessage!,
                valid.ErrorCode!
            );

        IReadOnlyDictionary<string, JsonElement> values =
            supplied ?? new Dictionary<string, JsonElement>();
        string? unknown = values.Keys.FirstOrDefault(name =>
            definitions.All(definition => definition.Name != name)
        );
        if (unknown is not null)
            return Result.Failure<Dictionary<string, string>>(
                $"Unknown pipeline input '{unknown}'.",
                "VALIDATION_FAILED"
            );

        Dictionary<string, string> bound = new(StringComparer.Ordinal);
        foreach (PipelineInputDefinition definition in definitions)
        {
            if (!values.TryGetValue(definition.Name, out JsonElement value))
            {
                if (definition.DefaultValue is { } fallback)
                    value = fallback;
                else if (definition.Required)
                    return Result.Failure<Dictionary<string, string>>(
                        $"Missing required pipeline input '{definition.Name}'.",
                        "VALIDATION_FAILED"
                    );
                else
                    continue;
            }
            if (!Matches(definition.Type, value))
                return Result.Failure<Dictionary<string, string>>(
                    $"Pipeline input '{definition.Name}' must be {definition.Type}.",
                    "VALIDATION_FAILED"
                );
            bound[definition.Name] =
                definition.Type == "text" ? value.GetString() ?? "" : value.GetRawText();
        }
        return Result.Success(bound);
    }

    public static Result<Dictionary<string, string>> BindStrings(
        IReadOnlyList<PipelineInputDefinition> definitions,
        IReadOnlyDictionary<string, string>? supplied
    )
    {
        Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
        foreach (PipelineInputDefinition definition in definitions)
        {
            if (supplied?.TryGetValue(definition.Name, out string? value) != true)
                continue;
            if (definition.Type == "text")
            {
                values[definition.Name] = JsonSerializer.SerializeToElement(value);
                continue;
            }
            try
            {
                using JsonDocument parsed = JsonDocument.Parse(value ?? string.Empty);
                values[definition.Name] = parsed.RootElement.Clone();
            }
            catch (JsonException)
            {
                return Result.Failure<Dictionary<string, string>>(
                    $"Pipeline input '{definition.Name}' must be {definition.Type}.",
                    "VALIDATION_FAILED"
                );
            }
        }
        return Bind(definitions, values);
    }

    private static bool Matches(string type, JsonElement value) =>
        type switch
        {
            "text" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "list" => value.ValueKind == JsonValueKind.Array,
            "object" => value.ValueKind == JsonValueKind.Object,
            _ => false,
        };
}
