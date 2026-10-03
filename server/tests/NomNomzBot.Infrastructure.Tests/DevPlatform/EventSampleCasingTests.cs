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
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// Every sample the event catalog serves, fixture-sourced or reflection-generated, must use the keys of the
/// payload schema served beside it: each sample key is a declared schema property, and each required schema
/// property is present in the sample.
/// </summary>
public sealed class EventSampleCasingTests
{
    [Fact]
    public void Every_catalog_sample_uses_the_keys_of_its_payload_schema()
    {
        IReadOnlyList<EventCatalogItemDto> catalog = new SdkTypeEmitter(
            new EventCatalog()
        ).EmitEventCatalog(SdkContext.Script);

        List<string> violations = [];
        foreach (EventCatalogItemDto item in catalog)
        {
            JsonObject sample = (JsonObject)JsonNode.Parse(item.SamplePayloadJson!)!;
            JsonObject schema = (JsonObject)item.PayloadSchema;
            JsonObject properties = (JsonObject)schema["properties"]!;

            List<string> unknown =
            [
                .. sample.Select(f => f.Key).Where(k => !properties.ContainsKey(k)),
            ];
            List<string> missing =
            [
                .. (schema["required"] as JsonArray ?? [])
                    .Select(n => n!.GetValue<string>())
                    .Where(n => !sample.ContainsKey(n)),
            ];
            if (unknown.Count > 0 || missing.Count > 0)
                violations.Add(
                    $"{item.WireName}: unknown [{string.Join(", ", unknown)}] missing [{string.Join(", ", missing)}]"
                );
        }

        string report =
            $"{violations.Count} of {catalog.Count} catalog samples differ from their schema:{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations);
        violations.Should().BeEmpty(report);
    }
}
