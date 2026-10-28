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
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Api.OpenApi;

namespace NomNomzBot.Api.Tests.Contract;

/// <summary>
/// "Dto" is a C# naming habit. The published contract (and the web types generated from it) names a type
/// for what it is, so no schema id in <c>server/openapi/v1.json</c> carries a "Dto" word, also not inside
/// a generic id such as <c>StatusResponseDtoOfFoo</c>.
/// </summary>
public sealed class OpenApiNoDtoSuffixTests
{
    private static readonly Regex DtoWord = new("Dto(?![a-z])", RegexOptions.Compiled);

    [Fact]
    public void No_schema_id_in_the_snapshot_carries_a_Dto_word()
    {
        JsonElement schemas = LoadSnapshot().GetProperty("components").GetProperty("schemas");

        List<string> offenders =
        [
            .. schemas.EnumerateObject().Select(s => s.Name).Where(name => DtoWord.IsMatch(name)),
        ];

        offenders
            .Should()
            .BeEmpty(
                "{0}",
                $"{offenders.Count} schema id(s) still carry a Dto word: {string.Join(", ", offenders.Take(10))}"
            );
    }

    [Fact]
    public void Schema_id_transform_drops_the_Dto_word_and_keeps_everything_else()
    {
        DtoSchemaIdName.Strip("ChannelDto").Should().Be("Channel");
        DtoSchemaIdName.Strip("StatusResponseDtoOfFooDto").Should().Be("StatusResponseOfFoo");
        DtoSchemaIdName.Strip("Dtos").Should().Be("Dtos");
        DtoSchemaIdName.Strip("Channel").Should().Be("Channel");
    }

    private static JsonElement LoadSnapshot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string path = Path.Combine(dir.FullName, "server", "openapi", "v1.json");
            if (!File.Exists(path))
                path = Path.Combine(dir.FullName, "openapi", "v1.json");
            if (File.Exists(path))
                return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
            dir = dir.Parent;
        }

        throw new FileNotFoundException("server/openapi/v1.json not found");
    }
}
