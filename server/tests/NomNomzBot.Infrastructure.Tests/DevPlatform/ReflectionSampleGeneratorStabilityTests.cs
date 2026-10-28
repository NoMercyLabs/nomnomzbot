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

using System.Text.Json.Nodes;
using FluentAssertions;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The sample payloads in the event catalogue must be identical across server restarts, so a string "...Id"
/// property gets an id derived from a stable hash of its name, never from a per-process randomized one.
/// </summary>
public sealed class ReflectionSampleGeneratorStabilityTests
{
    public sealed class TrackIdSample
    {
        public string TrackId { get; set; } = string.Empty;
    }

    [Fact]
    public void A_string_Id_property_gets_the_same_sample_id_in_every_process()
    {
        string json = ReflectionSampleGenerator.Generate(typeof(TrackIdSample), SdkContext.Script);

        string? id = ((JsonObject)JsonNode.Parse(json)!)["trackId"]?.GetValue<string>();

        id.Should().Be("id-40785");
    }
}
