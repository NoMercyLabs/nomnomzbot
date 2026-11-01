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

using System.Collections;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NomNomzBot.Api.Models;

namespace NomNomzBot.Api.Pagination;

/// <summary>
/// Publishes <c>take</c> and <c>page</c> on every list endpoint, so the typed client can ask for its own page.
/// <see cref="ListPaginationFilter"/> honours them at run time. An endpoint that already declares either
/// parameter keeps its own definition.
/// </summary>
public sealed class ListPaginationOpenApiTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (!IsListGet(context.Description))
            return Task.CompletedTask;

        operation.Parameters ??= [];
        AddIfMissing(
            operation,
            context.Description,
            "take",
            $"Page size, 1 to {ListPaginationFilter.MaxTake}."
        );
        AddIfMissing(operation, context.Description, "page", "Page number, starting at 1.");

        return Task.CompletedTask;
    }

    internal static bool IsListGet(ApiDescription description)
    {
        if (!string.Equals(description.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            return false;

        return description.SupportedResponseTypes.Any(r =>
            r.StatusCode == 200 && ReturnsList(r.Type)
        );
    }

    private static bool ReturnsList(Type? response)
    {
        if (response is null || !response.IsGenericType)
            return false;

        if (response.GetGenericTypeDefinition() != typeof(StatusResponseDto<>))
            return false;

        Type data = response.GetGenericArguments()[0];

        return data != typeof(string)
            && typeof(IEnumerable).IsAssignableFrom(data)
            && !typeof(IDictionary).IsAssignableFrom(data)
            && !data.GetInterfaces()
                .Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                );
    }

    private static void AddIfMissing(
        OpenApiOperation operation,
        ApiDescription description,
        string name,
        string text
    )
    {
        bool declared = description.ParameterDescriptions.Any(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
        );
        if (declared)
            return;

        operation.Parameters!.Add(
            new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Query,
                Required = false,
                Description = text,
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            }
        );
    }
}
