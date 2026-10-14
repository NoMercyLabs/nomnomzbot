// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace NomNomzBot.Api.OpenApi;

/// <summary>
/// Removes nullable properties from <c>required</c>. The API serializes with
/// <c>DefaultIgnoreCondition = WhenWritingNull</c>, so a null field is left out of the JSON; listing it as required
/// made the generated TypeScript types claim the field is always present. A nullable property is either typed
/// with the <c>null</c> flag (<c>["null", "string"]</c>) or a <c>oneOf</c>/<c>anyOf</c> that has a <c>null</c> branch.
/// </summary>
public sealed class OmitNullableRequiredSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (schema.Required is { Count: > 0 } required && schema.Properties is { Count: > 0 })
        {
            foreach (KeyValuePair<string, IOpenApiSchema> property in schema.Properties)
                if (AcceptsNull(property.Value))
                    required.Remove(property.Key);
        }

        return Task.CompletedTask;
    }

    private static bool AcceptsNull(IOpenApiSchema property) =>
        property.Type is { } type && type.HasFlag(JsonSchemaType.Null)
        || HasNullBranch(property.OneOf)
        || HasNullBranch(property.AnyOf);

    private static bool HasNullBranch(IList<IOpenApiSchema>? branches) =>
        branches is not null && branches.Any(branch => branch.Type == JsonSchemaType.Null);
}
