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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Tests.Commands;

public sealed class PipelineInputBindingTests
{
    [Fact]
    public void Required_input_is_refused_before_binding_any_values()
    {
        PipelineInputDefinition definition = new("blocks", "list", true, null, "Speech blocks");

        Result<Dictionary<string, string>> result = PipelineInputs.Bind(
            [definition],
            new Dictionary<string, JsonElement>()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("blocks");
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"name\":\"greeting\",\"type\":\"text\",\"required\":true}]")]
    [InlineData(
        "[{\"name\":\"greeting\",\"type\":\"text\",\"required\":true,\"description\":null}]"
    )]
    public void A_null_declaration_or_a_missing_description_from_request_json_is_refused_not_thrown(
        string json
    )
    {
        List<PipelineInputDefinition?> definitions =
            JsonSerializer.Deserialize<List<PipelineInputDefinition?>>(
                json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            ) ?? [];

        Result result = PipelineInputs.ValidateDeclaration(definitions);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Theory]
    [InlineData("text", "12")]
    [InlineData("number", "\"twelve\"")]
    [InlineData("boolean", "1")]
    [InlineData("list", "{}")]
    [InlineData("object", "[]")]
    public void Wrong_type_is_refused(string type, string json)
    {
        PipelineInputDefinition definition = new("value", type, true, null, "A value");
        Dictionary<string, JsonElement> supplied = new()
        {
            ["value"] = JsonDocument.Parse(json).RootElement.Clone(),
        };

        Result<Dictionary<string, string>> result = PipelineInputs.Bind([definition], supplied);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("value");
    }

    [Fact]
    public void Default_and_twelve_block_list_keep_their_shapes()
    {
        PipelineInputDefinition blocks = new("blocks", "list", true, null, "Speech blocks");
        PipelineInputDefinition voice = new(
            "voice",
            "text",
            false,
            JsonSerializer.SerializeToElement("narrator"),
            "Voice"
        );
        object[] twelve = Enumerable
            .Range(1, 12)
            .Select(i => (object)new { text = $"block {i}" })
            .ToArray();
        Dictionary<string, JsonElement> supplied = new()
        {
            ["blocks"] = JsonSerializer.SerializeToElement(twelve),
        };

        Result<Dictionary<string, string>> result = PipelineInputs.Bind([blocks, voice], supplied);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        JsonDocument.Parse(result.Value["blocks"]).RootElement.GetArrayLength().Should().Be(12);
        result.Value["voice"].Should().Be("narrator");
    }
}
