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

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// The input of the one SDK template call (<c>nnz.template.render</c> in scripts, <c>NomNomz.template.render</c> in
/// widgets): a template of at most <see cref="MaxTemplateLength"/> characters and a flat object of text, number and
/// boolean values that seed the render. Both surfaces read it here, so they accept and refuse the same input.
/// </summary>
public static class TemplateRenderInput
{
    public const int MaxTemplateLength = 4096;
    public const int MaxValues = 64;

    /// <summary>The seed variables of one render; a failure names what the caller must change.</summary>
    public static Result<Dictionary<string, string>> Parse(string? template, string? valuesJson)
    {
        if (string.IsNullOrEmpty(template))
            return Result.Failure<Dictionary<string, string>>(
                "A template is required.",
                "VALIDATION_FAILED"
            );
        if (template.Length > MaxTemplateLength)
            return Result.Failure<Dictionary<string, string>>(
                $"A template can be at most {MaxTemplateLength} characters.",
                "VALIDATION_FAILED"
            );

        Dictionary<string, string> seeds = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(valuesJson))
            return Result.Success(seeds);

        try
        {
            using JsonDocument document = JsonDocument.Parse(valuesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Invalid("The values must be an object of names and values.");

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                string? text = TextOf(property.Value);
                if (text is null)
                    return Invalid(
                        $"The value of '{property.Name}' must be text, a number or true or false."
                    );
                seeds[property.Name] = text;
            }
        }
        catch (JsonException)
        {
            return Invalid("The values are not valid JSON.");
        }

        return seeds.Count > MaxValues
            ? Invalid($"A render takes at most {MaxValues} values.")
            : Result.Success(seeds);
    }

    private static string? TextOf(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };

    private static Result<Dictionary<string, string>> Invalid(string message) =>
        Result.Failure<Dictionary<string, string>>(message, "VALIDATION_FAILED");
}
