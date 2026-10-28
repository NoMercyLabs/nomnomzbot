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

using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;

namespace NomNomzBot.Api.OpenApi;

/// <summary>
/// "Dto" is a C# habit, not part of what a type is. The published contract names a schema without it, so
/// the web types generated from it read <c>Channel</c>, not <c>ChannelDto</c>. C# class names keep the suffix.
/// </summary>
public static partial class DtoSchemaIdName
{
    /// <summary>Removes every "Dto" word (not followed by a lowercase letter) from a schema id.</summary>
    public static string Strip(string schemaId) => DtoWord().Replace(schemaId, string.Empty);

    /// <summary>The OpenAPI schema id: the framework default, without its "Dto" words.</summary>
    public static string? Create(JsonTypeInfo typeInfo)
    {
        string? defaultId = OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo);
        return defaultId is null ? null : Strip(defaultId);
    }

    [GeneratedRegex("Dto(?![a-z])")]
    private static partial Regex DtoWord();
}
