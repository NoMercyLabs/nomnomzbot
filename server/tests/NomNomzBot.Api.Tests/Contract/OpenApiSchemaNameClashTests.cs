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
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.OpenApi;

namespace NomNomzBot.Api.Tests.Contract;

/// <summary>
/// Microsoft.AspNetCore.OpenApi names a schema by the id the schema-id callback returns. Two different C#
/// types that reach the document under one id collapse into one schema, so a client reads the wrong shape.
/// The committed snapshot cannot show such a clash (it already holds the merged schema), so this test builds
/// the real OpenAPI document over the real controllers and records every type the generator names, through
/// <see cref="DtoSchemaIdName.Create"/>, the callback the deployed API registers.
/// </summary>
public sealed class OpenApiSchemaNameClashTests
{
    [Fact]
    public async Task No_two_types_reaching_the_OpenAPI_document_share_a_schema_name()
    {
        Dictionary<string, HashSet<Type>> byName = new(StringComparer.Ordinal);
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiControllers().AddApplicationPart(typeof(BaseController).Assembly);
        builder.Services.AddOpenApi(options =>
            options.CreateSchemaReferenceId = typeInfo =>
            {
                string? id = DtoSchemaIdName.Create(typeInfo);
                if (id is null)
                    return null;

                lock (byName)
                {
                    if (!byName.TryGetValue(id, out HashSet<Type>? types))
                        byName[id] = types = [];
                    types.Add(Nullable.GetUnderlyingType(typeInfo.Type) ?? typeInfo.Type);
                }

                return id;
            }
        );
        await using WebApplication app = builder.Build();
        app.MapOpenApi();
        app.MapControllers();
        await app.StartAsync();

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()
        );
        HashSet<string> published =
        [
            .. document
                .RootElement.GetProperty("components")
                .GetProperty("schemas")
                .EnumerateObject()
                .Select(schema => schema.Name),
        ];

        byName.Should().NotBeEmpty("the generator must have named the schemas of the API");
        published
            .Should()
            .Contain(byName.Keys, "every schema id the generator asked for is published");

        List<string> clashes =
        [
            .. byName
                .Where(entry => entry.Value.Count > 1)
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry =>
                    $"{entry.Key}: "
                    + string.Join(
                        " vs ",
                        entry
                            .Value.Select(type => type.FullName)
                            .OrderBy(n => n, StringComparer.Ordinal)
                    )
                ),
        ];

        clashes
            .Should()
            .BeEmpty(
                "{0}",
                $"{clashes.Count} schema name(s) are shared by different types: "
                    + string.Join(" | ", clashes)
                    + ". Rename one side so the name says what it is"
            );
    }
}
