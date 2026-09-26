// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
