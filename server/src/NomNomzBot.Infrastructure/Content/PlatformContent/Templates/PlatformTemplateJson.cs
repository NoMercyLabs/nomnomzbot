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

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// The one JSON contract every platform-template payload uses: camelCase property names (the dashboard's
/// wire shape), dictionary keys left as authored, unknown members ignored. <see cref="Hash{T}"/> serializes
/// through the same contract, so a payload and the same shape read back from a tenant row hash equal.
/// </summary>
internal static class PlatformTemplateJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
    };

    public static Result<T> Parse<T>(string? json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result.Failure<T>("The template payload is empty.", "VALIDATION_FAILED");

        try
        {
            T? parsed = JsonConvert.DeserializeObject<T>(json, Settings);
            return parsed is null
                ? Result.Failure<T>(
                    "The template payload is not a JSON object.",
                    "VALIDATION_FAILED"
                )
                : Result.Success(parsed);
        }
        catch (JsonException ex)
        {
            return Result.Failure<T>(
                $"The template payload is not valid JSON: {ex.Message}",
                "VALIDATION_FAILED"
            );
        }
    }

    public static string Hash<T>(T payload) =>
        PlatformContentHash.ComputeHash(JsonConvert.SerializeObject(payload, Settings));
}
