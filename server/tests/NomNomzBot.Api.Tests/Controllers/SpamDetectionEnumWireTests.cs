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
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves a spam detection's tier, confidence and outcomes travel as their names on the API wire. The dashboard
/// labels each chip by name ("Established", "High", "Flag"); a ladder number (60) matches nothing and every row
/// showed "New here" beside "Established viewer" (owner, 2026-10-06). The options mirror the MVC JSON setup in
/// <c>Program.cs</c>; the enums carry their own converter.
/// </summary>
public sealed class SpamDetectionEnumWireTests
{
    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void A_detection_serializes_its_tier_confidence_and_outcomes_as_names()
    {
        SpamDetectionDto dto = new(
            Guid.CreateVersion7(),
            "123",
            "viewer",
            "twitch",
            "msg-1",
            "hello",
            "",
            SpamConfidence.High,
            SpamTrustTier.Established,
            SpamOutcome.Flag,
            SpamOutcome.DeleteAndEscalate,
            true,
            "Established viewer",
            null,
            DateTime.UnixEpoch
        );

        string json = JsonSerializer.Serialize(dto, Wire);

        json.Should().Contain("\"confidence\":\"High\"");
        json.Should().Contain("\"tier\":\"Established\"");
        json.Should().Contain("\"outcome\":\"Flag\"");
        json.Should().Contain("\"wouldHaveBeen\":\"DeleteAndEscalate\"");
    }
}
