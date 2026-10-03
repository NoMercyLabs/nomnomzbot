// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
