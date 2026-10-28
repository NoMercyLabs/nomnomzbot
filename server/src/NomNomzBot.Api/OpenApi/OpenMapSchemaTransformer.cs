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

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace NomNomzBot.Api.OpenApi;

/// <summary>
/// Publishes a free-form map (<c>Dictionary&lt;string, object?&gt;</c>) as <c>additionalProperties: {}</c> (any
/// value). The schema generator renders an <c>object</c> value type as no schema at all, so the map came out as a
/// bare <c>{ "type": "object" }</c> and openapi-typescript typed it <c>Record&lt;string, never&gt;</c> — a map the
/// dashboard could not write a single value into.
/// </summary>
public sealed class OpenMapSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        bool isBareObject =
            schema.Type is { } type
            && type.HasFlag(JsonSchemaType.Object)
            && (schema.Properties is null || schema.Properties.Count == 0)
            && schema.AdditionalProperties is null
            && schema.AllOf is not { Count: > 0 }
            && schema.OneOf is not { Count: > 0 }
            && schema.AnyOf is not { Count: > 0 };

        if (isBareObject)
            schema.AdditionalProperties = new OpenApiSchema();

        return Task.CompletedTask;
    }
}
