// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.Enums;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the chat-filter enums travel as their names on the API wire, matching what the dashboard sends and
/// expects (<c>"Blocklist"</c>, <c>"Escalate"</c>), not as ordinals. The options mirror the MVC JSON setup in
/// <c>Program.cs</c> (camelCase, nulls omitted); the enums carry their own converter.
/// </summary>
public sealed class ChatFilterEnumWireTests
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void The_filter_dto_serializes_filterType_and_action_as_their_names()
    {
        ChatFilterDto dto = new(
            Guid.CreateVersion7(),
            ChatFilterType.Blocklist,
            "no-spam",
            null,
            ["buy followers"],
            ChatFilterAction.Escalate,
            null,
            4,
            true,
            false,
            0,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch
        );

        string json = JsonSerializer.Serialize(dto, Wire);

        json.Should().Contain("\"filterType\":\"Blocklist\"");
        json.Should().Contain("\"action\":\"Escalate\"");
    }

    [Fact]
    public void A_posted_action_name_reads_back_as_that_action()
    {
        const string body =
            """{"filterType":"Regex","name":"hold-links","action":"Hold","pattern":"x"}""";

        CreateChatFilterRequest? request = JsonSerializer.Deserialize<CreateChatFilterRequest>(
            body,
            Wire
        );

        request.Should().NotBeNull();
        request.Action.Should().Be(ChatFilterAction.Hold);
        request.FilterType.Should().Be(ChatFilterType.Regex);
    }
}
