// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Options;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Api.Tests.Identifiers;

/// <summary>
/// Live 2026-10-19, stoney_eagle's "Incoming raid": the bound pipeline showed as a raw id with "This target is not
/// in this channel". A binding target carries its command or pipeline id as a string, so the Guid converter never
/// saw it: it went out as a raw Guid while every list sends ULIDs, and a ULID picked from a list came back in where
/// the binder parses a Guid. The target id must speak the same owned-id form as every other id, both ways.
/// </summary>
public sealed class BindingTargetWireTests
{
    private static readonly Guid PipelineId = Guid.Parse("019f7867-37e7-7f46-9639-eea73b9c5bca");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000b7c1");

    /// <summary>The MVC body options, with the owned-id converters Program.cs adds.</summary>
    private static JsonSerializerOptions RestOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        OwnedIdJson.AddConverters(options);
        return options;
    }

    private static EventResponseDto RaidRow(BindingTarget target) =>
        new(
            Guid.Parse("019f15a5-3e09-7980-a75e-3ab3006a0628"),
            "channel.raid",
            true,
            "pipeline",
            null,
            PipelineId,
            [],
            DateTime.UtcNow,
            DateTime.UtcNow,
            false,
            false,
            [],
            target
        );

    [Fact]
    public void The_pipeline_a_row_runs_goes_out_in_the_same_ulid_form_as_the_pipeline_list()
    {
        string json = JsonSerializer.Serialize(
            RaidRow(new(BindingTargetKind.Pipeline, PipelineId.ToString())),
            RestOptions()
        );

        using JsonDocument doc = JsonDocument.Parse(json);
        string? targetId = doc.RootElement.GetProperty("target").GetProperty("id").GetString();
        targetId.Should().Be(GuidUlidCodec.Encode(PipelineId));
        targetId.Should().Be(doc.RootElement.GetProperty("pipelineId").GetString());
    }

    [Fact]
    public void A_command_target_goes_out_as_a_ulid_and_an_action_target_keeps_its_type_and_params()
    {
        JsonSerializerOptions options = RestOptions();
        Dictionary<string, JsonElement> settings = new()
        {
            ["message"] = JsonDocument.Parse("\"hi\"").RootElement,
        };

        string command = JsonSerializer.Serialize(
            new BindingTarget(BindingTargetKind.Command, CommandId.ToString()),
            options
        );
        string action = JsonSerializer.Serialize(
            new BindingTarget(BindingTargetKind.Action, null, "send_message", settings),
            options
        );

        command.Should().Be($$"""{"kind":0,"id":"{{GuidUlidCodec.Encode(CommandId)}}"}""");
        action.Should().Be("""{"kind":2,"actionType":"send_message","params":{"message":"hi"}}""");
    }

    [Fact]
    public void A_ulid_picked_from_a_list_comes_in_as_the_guid_the_binder_parses()
    {
        string body = $$$"""{"target":{"kind":1,"id":"{{{GuidUlidCodec.Encode(PipelineId)}}}"}}""";

        UpdateEventResponseDto? request = JsonSerializer.Deserialize<UpdateEventResponseDto>(
            body,
            RestOptions()
        );

        request!
            .Target.Should()
            .Be(new BindingTarget(BindingTargetKind.Pipeline, PipelineId.ToString()));
    }

    [Theory]
    [InlineData(1, "019f7867-37e7-7f46-9639-eea73b9c5bca")]
    [InlineData(1, "")]
    [InlineData(0, "song_request")]
    public void A_raw_guid_an_empty_clear_and_a_built_in_key_come_in_unchanged(int kind, string id)
    {
        string body = $$$"""{"target":{"kind":{{{kind}}},"id":"{{{id}}}"}}""";

        UpdateEventResponseDto? request = JsonSerializer.Deserialize<UpdateEventResponseDto>(
            body,
            RestOptions()
        );

        request!.Target.Should().Be(new BindingTarget((BindingTargetKind)kind, id));
    }

    [Fact]
    public void The_hub_wire_sends_the_target_id_in_the_same_ulid_form_as_rest()
    {
        JsonHubProtocolOptions hubOptions = new();
        OwnedIdJson.AddConverters(hubOptions.PayloadSerializerOptions);
        JsonHubProtocol protocol = new(Options.Create(hubOptions));

        InvocationMessage message = new(
            "ConfigChanged",
            [RaidRow(new(BindingTargetKind.Pipeline, PipelineId.ToString()))]
        );
        string json = Encoding.UTF8.GetString(protocol.GetMessageBytes(message).ToArray());

        json.Should().Contain(GuidUlidCodec.Encode(PipelineId));
        json.Should().NotContain(PipelineId.ToString());
    }
}
