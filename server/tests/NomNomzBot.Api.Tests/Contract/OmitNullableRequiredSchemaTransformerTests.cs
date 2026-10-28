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

using FluentAssertions;
using Microsoft.OpenApi;
using NomNomzBot.Api.OpenApi;

namespace NomNomzBot.Api.Tests.Contract;

/// <summary>
/// The API serializes with <c>WhenWritingNull</c>, so a null field is absent from the JSON. The published schema
/// must therefore not list a nullable property as required.
/// </summary>
public sealed class OmitNullableRequiredSchemaTransformerTests
{
    [Fact]
    public async Task A_nullable_property_is_dropped_from_required_and_a_non_nullable_one_stays()
    {
        OpenApiSchema schema = new()
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["createdAt"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String | JsonSchemaType.Null,
                },
                ["owner"] = new OpenApiSchema
                {
                    OneOf =
                    [
                        new OpenApiSchema { Type = JsonSchemaType.Null },
                        new OpenApiSchema { Type = JsonSchemaType.Object },
                    ],
                },
                ["name"] = new OpenApiSchema { Type = JsonSchemaType.String },
            },
            Required = new HashSet<string> { "createdAt", "owner", "name" },
        };

        await new OmitNullableRequiredSchemaTransformer().TransformAsync(
            schema,
            null!,
            CancellationToken.None
        );

        schema.Required.Should().BeEquivalentTo(["name"]);
        schema.Properties.Should().ContainKeys("createdAt", "owner", "name");
    }
}
