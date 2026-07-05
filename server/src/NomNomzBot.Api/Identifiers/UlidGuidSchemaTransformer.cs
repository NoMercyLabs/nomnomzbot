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

namespace NomNomzBot.Api.Identifiers;

/// <summary>
/// Makes the generated OpenAPI contract describe owned ids the way they actually go over the wire. The schema
/// generator maps a CLR <see cref="Guid"/> to <c>{ type: string, format: uuid }</c>, but the boundary encodes it as
/// a 26-char Crockford base32 ULID (<see cref="UlidGuidJsonConverter"/>), so this transformer rewrites the
/// <c>uuid</c> format to <c>ulid</c> and documents it — keeping the committed <c>openapi/v1.json</c> snapshot
/// truthful for the frontend's generated DTOs.
/// </summary>
public sealed class UlidGuidSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.JsonTypeInfo.Type == typeof(Guid) || context.JsonTypeInfo.Type == typeof(Guid?))
        {
            schema.Format = "ulid";
            schema.Description =
                "Owned identifier — a 26-character Crockford base32 ULID (the API-boundary encoding of the "
                + "server's UUIDv7 storage key). Accepted inbound as either this ULID or a raw GUID string.";
        }

        return Task.CompletedTask;
    }
}
