// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
